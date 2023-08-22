# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

Describe "Subsystem tests" {
    BeforeAll {
        $subsystemResults = Get-PSSubsystem
    }

    It "The CommandPredictor should include the PSAdapter" {
        $subsystemResults.where({$_.kind -eq "CommandPredictor"}).Implementations.Name | Should -Contain "PSAdapter"
    }

    It "The FeedbackProvider should include the PSAdapter" {
        $subsystemResults.where({$_.kind -eq "FeedbackProvider"}).Implementations.Name | Should -Contain "PSAdapter"
    }

}
