' Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

Imports Microsoft.CodeAnalysis.CodeFixes
Imports Microsoft.CodeAnalysis.Diagnostics

Module Verifier
    Public Function Create(Of TAnalyzer As {DiagnosticAnalyzer, New}, TCodeFix As {CodeFixProvider, New})() As Verifier(Of TAnalyzer, TCodeFix)
        Return New Verifier(Of TAnalyzer, TCodeFix)
    End Function
End Module
