# JsonAdapter

This is a FeedbackProvider and SuggestionPredictor for native utilities which
have a json adapter written for them, or if the `jc` utility is installed suggestions
on how it may be incorporated into the users command line.

The following is a transcript where the `uname` command is used and has a
`uname-json` script which can convert the output to an object, as well as
how `jc` can be used to transform the text output into an object suitable
for use with PowerShell.

This module will work only with PowerShell 7.4 preview 3 or newer.

```powershell
PS> ^C                    
PS> pwsh-preview
PS> import-module Microsoft.PowerShell.JsonAdapter
PS> set-psReadLineOption -PredictionViewStyle ListView
PS> uname -a 
> uname -a                                                                                 [History]
> uname | jc --uname | ConvertFrom-Json                                                [JsonAdapter]
> uname | uname-json                                                                   [JsonAdapter]
Darwin JamesiMac20.local 22.5.0 Darwin Kernel Version 22.5.0: Thu Jun  8 22:22:22 PDT 2023; root:xnu-8796.121.3~7/RELEASE_X86_64 x86_64

[JsonAdapter]
  Json adapter found additional ways to run.
    ➤ uname -a | jc --uname | ConvertFrom-Json
    ➤ uname -a | uname-json

PS/JsonAdapter> uname -a | jc --uname | ConvertFrom-Json

machine        : x86_64
kernel_name    : Darwin
node_name      : JamesiMac20.local
kernel_release : 22.5.0
kernel_version : Darwin Kernel Version 22.5.0: Thu Jun 8 22:22:22 PDT 2023; root:xnu-8796.121.3~7/RELEASE_X86_64

PS>
```
