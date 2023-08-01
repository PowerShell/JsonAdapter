// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Management.Automation;
using System.Management.Automation.Language;
using System.Threading.Tasks;

namespace PSAdapterProvider
{
    public class SuggestionGenerator
    {
        private PowerShell _ps;
        private CommandInvocationIntrinsics _cIntrinsics;
        private ConcurrentDictionary<string, string> _commandAdapterCache { get; set; }
        private ConcurrentBag<string> _nativeCommandCache { get; set; }
        private CommandTypes allowedAdapterTypes = CommandTypes.Application | CommandTypes.ExternalScript | CommandTypes.Script;
        private bool hasJcCommand = false;
        // supported commands for jc
        private readonly HashSet<string> _jcCommands = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
            "arp", "cksum", "crontab", "date", "df", "dig", "dir", "du", "file", "finger",
            "free", "hash", "id", "ifconfig", "iostat", "jobs", "lsof", "mount", "mpstat",
            "netstat", "route", "stat", "sysctl", "traceroute", "uname", "uptime", "w", "wc",
            "who", "zipinfo"
            };

        public SuggestionGenerator()
        {
            _ps = PowerShell.Create(RunspaceMode.NewRunspace);
            _cIntrinsics = _ps.Runspace.SessionStateProxy.InvokeCommand;
            hasJcCommand = CheckJc();
            _commandAdapterCache = new ConcurrentDictionary<string, string>();
            _nativeCommandCache = new ConcurrentBag<string>();
        }

        // This both returns and sets the value of hasJcCommand
        // It can be used to check if the jc command is available after the constructor has been called
        public bool CheckJc(bool? defaultValue = null)
        {
            if (defaultValue.HasValue)
            {
                hasJcCommand = defaultValue.Value;
                return hasJcCommand;
            }

            if(null != _cIntrinsics.GetCommand("jc", CommandTypes.Application))
            {
                hasJcCommand = true;
                return true;
            }
            hasJcCommand = false;
            return false;
        }

        public void Dispose()
        {
            _ps.Dispose();
        }

        private void TryAddJcAdapter(string commandName)
        {
            if (_jcCommands.TryGetValue(commandName, out string? adapterCommand))
            {
                _commandAdapterCache.TryAdd("jc:" + commandName, "jc --" + adapterCommand + " | ConvertFrom-Json");
            }
        }

        private void TryAddJsonAdapter(string commandName)
        {
            var jsonAdapter = commandName + "-adapter";
            var cmdInfo = _cIntrinsics.GetCommand(jsonAdapter, allowedAdapterTypes);
            if (null != cmdInfo)
            {
                _commandAdapterCache.TryAdd("json:" + commandName, jsonAdapter);
            }
        }

        public void ClearAdapterCache()
        {
            _commandAdapterCache.Clear();
        }

        // this is public for testing purposes
        // We don't really have time to find the adapter, so if it's not in the cache, we'll return an empty list.
        // We should find something the second time around, if it exists
        public List<string>? GetSuggestions(CommandAst cAst)
        {
            List<string> suggestions = new List<string>();
            string commandName = cAst.GetCommandName();
            if (null == commandName)
            {
                return null;
            }

            var commandWithoutExtension = Path.GetFileNameWithoutExtension(commandName);
            
            // only return suggestions on external scripts or applications
            // check the cache first to see if we've already found the command
            // we may get cancelled before we complete the following checks, but we should be populating the caches.
            if(! _nativeCommandCache.Contains(commandWithoutExtension))
            {
                var cmdInfo = _cIntrinsics.GetCommand(commandWithoutExtension, CommandTypes.ExternalScript | CommandTypes.Application);
                if (cmdInfo == null)
                {
                    return null;
                }
                _nativeCommandCache.Add(commandWithoutExtension);
            }

            string? adapter;
            if (hasJcCommand && _commandAdapterCache.TryGetValue("jc:" + commandWithoutExtension, out adapter))
            {
                suggestions.Add(cAst.Extent.Text + " | " + adapter);
            }
            else
            {
                Task.Run(() => TryAddJcAdapter(commandWithoutExtension));
                // TryAddJcAdapter(commandWithoutExtension);
            }

            // we need to check if the command has an adapter with the shape <name>-adapter.*
            var jsonAdapter = commandWithoutExtension + "-adapter";
            if (_commandAdapterCache.TryGetValue("json:" + commandName, out adapter))
            {
                suggestions.Add(cAst.Extent.Text + " | " + adapter);
            }
            else
            {
                Task.Run(() => TryAddJsonAdapter(commandWithoutExtension));
                // TryAddJsonAdapter(commandWithoutExtension);
            }
            
            return suggestions;
        }

        public List<PipelineAst> GetSuggestedPipelines(CommandAst cAst)
        {
            List<PipelineAst> pipelines = new List<PipelineAst>();
            
            foreach(string suggestion in GetSuggestions(cAst) ?? new List<string>())
            {
                PipelineAst? pAst = Parser.ParseInput(suggestion, out _, out ParseError[] errors).Find(ast => ast is PipelineAst, false) as PipelineAst;
                if (errors.Length == 0 && null != pAst)
                {
                    pipelines.Add(pAst);
                }
            }

            return pipelines;
        }
    }
}
