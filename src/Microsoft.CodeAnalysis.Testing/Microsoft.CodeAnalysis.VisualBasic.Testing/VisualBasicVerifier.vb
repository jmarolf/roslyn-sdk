' Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

Imports Microsoft.CodeAnalysis.CodeFixes
Imports Microsoft.CodeAnalysis.Diagnostics
Imports Microsoft.CodeAnalysis.Testing

Public Class VisualBasicVerifier(Of TAnalyzer As {DiagnosticAnalyzer, New}, TCodeFix As {CodeFixProvider, New}, TVerifier As {IVerifier, New})
    Inherits Verifier(Of TAnalyzer, TCodeFix, VisualBasicTest(Of TAnalyzer, TCodeFix, TVerifier), TVerifier)
End Class
