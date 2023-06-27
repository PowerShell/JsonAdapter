using System;
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

        private SuggestionGenerator _suggestionGenerator;

        Dictionary<string, string>? ISubsystem.FunctionsToDefine => null;

        /// <summary>
        /// add counter for cancellation token
        /// </summary>
        public static int FeedbackCancelCount { get; set; }

        /// <summary>
        /// add counter for cancellation token
        /// </summary>
        public static int SuggestionCancelCount { get; set; }
        public static int SuggestionRequestedCount { get; set; }

        /// <summary>
        /// Trigger for calling the predictor
        /// </summary>
        public FeedbackTrigger Trigger => FeedbackTrigger.All;

        private int suggestionAccepted = 0;
        private int suggestionDisplayed = 0;
        private int commandLineAccepted = 0;
        private int commandLineExecuted = 0;

        public static JsonAdapterFeedbackPredictor Singleton { get; } = new JsonAdapterFeedbackPredictor(Init.id);

        public JsonAdapterFeedbackPredictor(string? guid = null)
        {
            if (guid is null) {
                _guid = Guid.NewGuid();
            } else {
                _guid = new Guid(guid);
            }

            _suggestionGenerator = new SuggestionGenerator();
        }

        public void Dispose()
        {
            _suggestionGenerator.Dispose();
        }

        public Guid Id => _guid;

        public string Name => "JsonAdapter";

        public string Description => "Finds a JSON adapter for a native application.";

        /// <summary>
        /// Get feedback.
        /// </summary>
        public FeedbackItem? GetFeedback(FeedbackContext context, CancellationToken token)
        {
            CommandAst? cAst = context.CommandLineAst.Find((ast) => ast is CommandAst, true) as CommandAst;
            if (cAst is not null)
            {
                /*
                // we need pipelines here because we need to check the potential next command in the pipeline
                // because if one of them is what we suggest, we don't want to suggest anything
                List<PipelineAst>? suggestedPipelines = _suggestionGenerator.GetSuggestedPipelines(cAst);
                if (suggestedPipelines is null || suggestedPipelines.Count == 0)
                {
                    return null;
                }

                // Get the second command if it exists and compare it to the second command of the suggested pipelines
                PipelineAst? parent = cAst.Parent as PipelineAst;
                if (parent is not null && parent.PipelineElements.Count > 1)
                {
                    string? secondCommand = null;
                    secondCommand = (parent.PipelineElements[1] as CommandAst)?.GetCommandName();
                    if (secondCommand is not null)
                    {
                        foreach (PipelineAst suggestion in suggestedPipelines)
                        {
                            string? suggestionSecondCommand = (suggestion.PipelineElements[1] as CommandAst)?.GetCommandName();
                            if (suggestionSecondCommand is not null && suggestionSecondCommand.Equals(secondCommand))
                            {
                                return null;
                            }
                        }
                    }
                }

                List<string> suggestions = new List<string>(suggestedPipelines.Count);
                foreach(PipelineAst suggestion in suggestedPipelines)
                {
                    suggestions.Add(suggestion.Extent.Text);
                }
                */

                List<string>? filteredSuggestions = GetFilteredSuggestions(cAst);
                if (filteredSuggestions is null)
                {
                    return null;
                }
                
                return new FeedbackItem("Json adapter found additional ways to run.", filteredSuggestions);
            }

            return null;
        }

        public List<string>? GetFilteredSuggestions(CommandAst cAst)
        {
            // we need pipelines here because we need to check the potential next command in the pipeline
            // because if one of them is what we suggest, we don't want to suggest anything
            List<PipelineAst>? suggestedPipelines = _suggestionGenerator.GetSuggestedPipelines(cAst);
            if (suggestedPipelines is null || suggestedPipelines.Count == 0)
            {
                return null;
            }

            // Get the second command if it exists and compare it to the second command of the suggested pipelines
            PipelineAst? parent = cAst.Parent as PipelineAst;
            if (parent is not null && parent.PipelineElements.Count > 1)
            {
                string? secondCommand = null;
                secondCommand = (parent.PipelineElements[1] as CommandAst)?.GetCommandName();
                if (secondCommand is not null)
                {
                    foreach (PipelineAst suggestion in suggestedPipelines)
                    {
                        string? suggestionSecondCommand = (suggestion.PipelineElements[1] as CommandAst)?.GetCommandName();
                        if (suggestionSecondCommand is not null && suggestionSecondCommand.Equals(secondCommand))
                        {
                            return null;
                        }
                    }
                }
            }

            List<string> suggestions = new List<string>(suggestedPipelines.Count);
            foreach(PipelineAst suggestion in suggestedPipelines)
            {
                suggestions.Add(suggestion.Extent.Text);
            }
            return suggestions;

        }

        private List<PredictiveSuggestion>? GetSuggestions(CommandAst commandAst)
        {
            List<PredictiveSuggestion> suggestionList = new List<PredictiveSuggestion>(1);
            string commandName = commandAst.GetCommandName();

            List<PipelineAst>? suggestions = _suggestionGenerator.GetSuggestedPipelines(commandAst);
            if (suggestions is null)
            {
                return null;
            }

            foreach(PipelineAst suggestion in suggestions)
            {
                suggestionList.Add(new PredictiveSuggestion(suggestion.Extent.Text));
            }

            return suggestionList;
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
            SuggestionRequestedCount++;
            CommandAst? commandAst = context.InputAst.Find((ast) => ast is CommandAst, true) as CommandAst;
            if(commandAst is null)
            {
                return default;
            }

            List<PipelineAst>? suggestions = _suggestionGenerator.GetSuggestedPipelines(commandAst);
            if (suggestions is null)
            {
                return default;
            }

            List<PredictiveSuggestion> result = new List<PredictiveSuggestion>(suggestions.Count);
            foreach(PipelineAst suggestion in suggestions)
            {
                result.Add(new PredictiveSuggestion(suggestion.Extent.Text));
            }

            if (cancellationToken.IsCancellationRequested)
            {
                SuggestionCancelCount++;
            }

            return new SuggestionPackage(result);
        }

        public void OnCommandLineAccepted(PredictionClient client, IReadOnlyList<string> history)
        {
            commandLineAccepted++;
        }

        public void OnSuggestionDisplayed(PredictionClient client, uint session, int countOrIndex) {
            suggestionDisplayed++;
        }

        public void OnSuggestionAccepted(PredictionClient client, uint session, string acceptedSuggestion) {
            suggestionAccepted++;
         }

        public void OnCommandLineExecuted(PredictionClient client, string commandLine, bool success) {
            commandLineExecuted++;
        }

    }
}
