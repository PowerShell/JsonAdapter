using System.Diagnostics;
using System.Collections.ObjectModel;
using System.IO;
using System.Management.Automation;
using System.Collections.Concurrent;
using System.Management.Automation.Language;
using System.Management.Automation.Runspaces;
using System.Management.Automation.Subsystem;
using System.Management.Automation.Subsystem.Feedback;
using System.Management.Automation.Subsystem.Prediction;

namespace JsonAdapterProvider
{
    public sealed class Init : IModuleAssemblyInitializer, IModuleAssemblyCleanup
    {
        internal const string id = "6edf7436-db79-4b5b-b889-4e6d6a1c8680";

        public void OnImport()
        {
            SubsystemManager.RegisterSubsystem<IFeedbackProvider, JsonAdapterFeedbackPredictor>(JsonAdapterFeedbackPredictor.Singleton);
            SubsystemManager.RegisterSubsystem<ICommandPredictor, JsonAdapterFeedbackPredictor>(JsonAdapterFeedbackPredictor.Singleton);
        }

        public void OnRemove(PSModuleInfo psModuleInfo)
        {
            SubsystemManager.UnregisterSubsystem<IFeedbackProvider>(new Guid(id));
            SubsystemManager.UnregisterSubsystem<ICommandPredictor>(new Guid(id));
        }
    }

    public sealed class JsonAdapterFeedbackPredictor : IFeedbackProvider, ICommandPredictor
    {
        private readonly Guid _guid;
        private string? _suggestion;
        private Runspace _rs;
        private CommandInvocationIntrinsics _cIntrinsics;
        Dictionary<string, string>? ISubsystem.FunctionsToDefine => null;

        private ConcurrentDictionary<string, string> _commandCache { get; set; }

        private CommandTypes allowedAdapterTypes = CommandTypes.Application | CommandTypes.Function | CommandTypes.Filter |
            CommandTypes.Alias | CommandTypes.ExternalScript | CommandTypes.Script;

        /// <summary>
        /// add counter for cancellation token
        /// </summary>
        public static int FeedbackCancelCount { get; set; }

        /// <summary>
        /// add counter for cancellation token
        /// </summary>
        public static int SuggestionCancelCount { get; set; }

        /// <summary>
        /// Trigger for calling the predictor
        /// </summary>
        public FeedbackTrigger Trigger => FeedbackTrigger.All;

        // supported commands for jc
        private readonly HashSet<string> _jcCommands = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
            "arp",
            "cksum",
            "crontab",
            "date",
            "df",
            "dig",
            "dir",
            "du",
            "file",
            "finger",
            "free",
            "hash",
            "id",
            "ifconfig",
            "iostat",
            "jobs",
            "lsof",
            "mount",
            "mpstat",
            "netstat",
            "route",
            "stat",
            "sysctl",
            "traceroute",
            "uname",
            "uptime",
            "w",
            "wc",
            "who",
            "zipinfo"
            };

        private int accepted = 0;
        private int displayed = 0;
        private int executed = 0;

        private bool hasJcCommand = false;

        public static JsonAdapterFeedbackPredictor Singleton { get; } = new JsonAdapterFeedbackPredictor(Init.id);

        public JsonAdapterFeedbackPredictor(string? guid = null)
        {
            if (guid is null) {
                _guid = Guid.NewGuid();
            } else {
                _guid = new Guid(guid);
            }

            _rs = RunspaceFactory.CreateRunspace(InitialSessionState.CreateDefault());
            _rs.Open();
            _cIntrinsics = _rs.SessionStateProxy.InvokeCommand;
            if(_cIntrinsics.GetCommand("jc", CommandTypes.Application) is not null)
            {
                hasJcCommand = true;
            }
            _commandCache = new ConcurrentDictionary<string, string>();
        }

        public void Dispose()
        {
            _rs.Dispose();
        }

        public Guid Id => _guid;

        public string Name => "JsonAdapter";

        public string Description => "Finds a JSON adapter for a native application.";

        /// <summary>
        /// Get feedback.
        /// </summary>
        public FeedbackItem? GetFeedback(FeedbackContext context, CancellationToken token)
        {
            return GetFeedback(context.CommandLine, token);
        }

        /// <summary>
        /// Gets feedback based on the given command line and error record.
        /// </summary>
        public FeedbackItem? GetFeedback(string commandLine, ErrorRecord er, CancellationToken token)
        {
            return GetFeedback(commandLine, token);
        }

        private List<PredictiveSuggestion>? GetSuggestions(CommandAst commandAst)
        {
            List<PredictiveSuggestion> suggestions = new List<PredictiveSuggestion>(1);
            string commandName = commandAst.GetCommandName();

            var command = _cIntrinsics.GetCommand(commandName, CommandTypes.Application|CommandTypes.ExternalScript);
            if (command is null)
            {
                return null;
            }
            
            // hunt for <name>-json style adapter
            var adapterCmd = string.Format("{0}-json", Path.GetFileNameWithoutExtension(commandName));
            var adapter = _cIntrinsics.GetCommand(adapterCmd, allowedAdapterTypes);
            if (adapter is not null)
            {
                _commandCache.TryAdd("json:"+commandName, adapterCmd);
                suggestions.Add(new PredictiveSuggestion(string.Format("{0} | {1}", commandAst.Extent.Text, adapterCmd)));
            }

            if (hasJcCommand && _jcCommands.Contains(commandName))
            {
                var adapterPipeline = string.Format("{0} | ConvertFrom-Json", GetJcAdapter(commandName));
                _commandCache.TryAdd("jc:" + commandName, adapterPipeline);
                suggestions.Add(new PredictiveSuggestion(string.Format("{0} | {1}", commandAst.Extent.Text, adapterPipeline)));
            }

            return suggestions;
        }

        /// <summary>
        /// Gets feedback based on the given command line and error record.
        /// </summary>
        public FeedbackItem? GetFeedback(string commandLine, CancellationToken token)
        {
            List<string> pipelineElements = new List<string>();
            Ast myAst = Parser.ParseInput(commandLine, out _, out _);
            bool adapterFound = false;

            foreach(var cAst in myAst.FindAll((ast) => ast is CommandAst, true))
            {
                var commandAst = (CommandAst)cAst;
                var commandName = commandAst.GetCommandName();
                if (commandName is null)
                {
                    continue;
                }

                // search only for Native applications and Native scripts
                var command = _cIntrinsics.GetCommand(commandName, CommandTypes.Application|CommandTypes.ExternalScript);
                if (command is null)
                {
                    continue;
                }

                // Check if the command has a JSON adapter
                // need to remove the .exe extension before searching for the adapter
                var adapterCmd = string.Format("{0}-json", Path.GetFileNameWithoutExtension(commandName));
                var adapter = _cIntrinsics.GetCommand(adapterCmd, allowedAdapterTypes);
                // We haven't found a "-json" adapter for this command
                if (adapter is null)
                {
                    pipelineElements.Add(cAst.Extent.Text);
                    // look for a jc adapter
                    string? JcCommand = GetJcAdapter(commandName);
                    if (JcCommand is not null)
                    {
                        pipelineElements.Add(JcCommand);
                        pipelineElements.Add("ConvertFrom-Json");
                        adapterFound = true;
                    }
                    continue;
                }

                // construct the pipeline to display to the user.
                pipelineElements.Add(string.Format("{0} | {1}", cAst.Extent.Text, adapterCmd));
                adapterFound = true;
            }

            if (!adapterFound)
            {
                return null;
            }

            // Rewrite the command line to use the adapter
            var pipeline = string.Join(" | ", pipelineElements);
            return new FeedbackItem(
                "A JSON adapter was found for this command.",
                new List<string> { pipeline }
                );

        }

        public string? GetJcAdapter(string command)
        {
            if (_jcCommands.Contains(command))
            {
                return string.Format("jc --{0}", command);
            }
            return null;
        }

        public bool CanAcceptFeedback(PredictionClient client, PredictorFeedbackKind feedback)
        {
            return feedback switch
            {
                PredictorFeedbackKind.CommandLineAccepted => true,
                _ => false,
            };
        }

        public SuggestionPackage GetSuggestion(PredictionClient client, PredictionContext context, CancellationToken cancellationToken)
        {
            // return new SuggestionPackage(new List<PredictiveSuggestion>(){new PredictiveSuggestion("lol - too slow")});
            
            List<PredictiveSuggestion>? result = null;

            result ??= new List<PredictiveSuggestion>(1);

            CommandAst? commandAst = context.InputAst.Find((ast) => ast is CommandAst, true) as CommandAst;

            if(commandAst is null)
            {
                return default;
            }

            string commandName; // result.Add(new PredictiveSuggestion(commandAst.GetCommandName()));
            if ((commandName = commandAst.GetCommandName()) is null)
            {
                return default;
            }

            bool cacheHit = false;
            if (_commandCache.TryGetValue("json:"+commandName, out string? jsonAdapter))
            {
                result.Add(new PredictiveSuggestion(string.Format("{0} | {1}", commandAst.Extent.Text, jsonAdapter)));
                cacheHit = true;
            }

            if (_commandCache.TryGetValue("jc:"+commandName, out string? jcAdapter))
            {
                result.Add(new PredictiveSuggestion(string.Format("{0} | {1}", commandAst.Extent.Text, jcAdapter)));
                cacheHit = true;
            }

            if (cacheHit)
            {
                return new SuggestionPackage(result);
            }
            
            // This could time out, but we will try to get the suggestions anyway because we need to cache them.
            result = GetSuggestions(commandAst);
            if (result is not null)
            {
                return new SuggestionPackage(result);
            }
            /*
            if (result is null || result.Count == 0)
            {
                return default;
            }

            if (result is not null)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    SuggestionCancelCount++;
                }

                return new SuggestionPackage(result);
            }

            */
            return default;
        }

        public void OnCommandLineAccepted(PredictionClient client, IReadOnlyList<string> history)
        {
            _suggestion = null;
        }

        public void OnSuggestionDisplayed(PredictionClient client, uint session, int countOrIndex) {
            displayed++;
        }

        public void OnSuggestionAccepted(PredictionClient client, uint session, string acceptedSuggestion) {
            accepted++;
         }

        public void OnCommandLineExecuted(PredictionClient client, string commandLine, bool success) {
            executed++;
        }

        public string TestPrediction(string commandLine)
        {
            CancellationTokenSource source = new CancellationTokenSource();
            CancellationToken token = source.Token;
            FeedbackItem? fi = GetFeedback(commandLine, token);
            if (fi is null || fi.RecommendedActions is null || fi.RecommendedActions.Count < 1)
            {
                return string.Empty;
            }
            return fi.RecommendedActions[0];
        }

    }
}
