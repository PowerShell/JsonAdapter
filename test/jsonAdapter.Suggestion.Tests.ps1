# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

Describe "Suggestion Tests" {
    BeforeAll {
        # this uses reflection to get at the generator
        $singleton = [JsonAdapterProvider.JsonAdapterFeedbackPredictor]::Singleton
        $generator = $singleton.gettype().getfield("_suggestionGenerator", "NonPublic,Instance").GetValue($singleton)
        # arp seems to be a binary on both windows and *nix
        $Ast1 = { arp }.Ast.Find({$args[0] -is [System.Management.Automation.Language.CommandAst]}, $false)
        $Ast2 = { arp | jc --arp | ConvertFrom-Json }.Ast.Find({$args[0] -is [System.Management.Automation.Language.CommandAst]}, $false)
        $Ast3 = { arp | arp-json }.Ast.Find({$args[0] -is [System.Management.Automation.language.CommandAst]}, $false)
        $Ast4 = { arp -abc | jc --arp }.Ast.Find({$args[0] -is [System.Management.Automation.Language.CommandAst]}, $false)

        $savedPath = $env:PATH
        $env:PATH += "$([io.Path]::PathSeparator)$TESTDRIVE"
        '"output"' > "${TESTDRIVE}/arp-json.ps1"

        # the first call will always miss the cache, so call it here
        # it is also timing sensitive (because we are hitting the file system for the script adapter), so add a sleep
        $generator.GetSuggestions($Ast1) | Out-Null
        start-sleep 2
        $generator.GetSuggestions($Ast1) | Out-Null
    }

    AfterAll {
        $env:PATH = $savedPath
    }

    It "Should provide a jc suggestion for '$ast1'" {
        $generator.GetSuggestions($Ast1) | Should -Contain "arp | jc --arp | ConvertFrom-Json"
    }

    It "Should not provide a jc suggestion for '$ast2'" {
        $singleton.GetFilteredSuggestions($Ast2) | Should -BeNullOrEmpty
    }

    It "Should provide an arp-json suggestion for '$ast3'" {
        $suggestions = $generator.GetSuggestions($Ast3)
        $suggestions | Should -Contain "arp | arp-json"
    }

    It "Should preserve the parameters of '$Ast4'" {
        $suggestions = $generator.GetSuggestions($Ast4)
        $matches = $suggestions | Where-Object { $_ -match "abc" }
        $matches.Count | Should -Be 2
        
    }

}
