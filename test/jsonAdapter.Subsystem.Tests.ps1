# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

Describe "Subsystem tests" {
    BeforeAll {
        $subsystemResults = Get-PSSubsystem
    }

    It "The CommandPredictor should include the jsonAdapter" {
        $subsystemResults.where({$_.kind -eq "CommandPredictor"}).Implementations.Name | Should -Contain "jsonAdapter"
    }

    It "The FeedbackProvider should include the jsonAdapter" {
        $subsystemResults.where({$_.kind -eq "FeedbackProvider"}).Implementations.Name | Should -Contain "jsonAdapter"
    }

}
