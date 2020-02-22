// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Microsoft.CodeAnalysis.Testing
{
    /// <summary>
    /// A default verifier for diagnostic analyzers with code fixes.
    /// </summary>
    /// <typeparam name="TAnalyzer">The <see cref="DiagnosticAnalyzer"/> to test.</typeparam>
    /// <typeparam name="TCodeFix">The <see cref="CodeFixProvider"/> to test.</typeparam>
    /// <typeparam name="TTest">The test implementation to use.</typeparam>
    /// <typeparam name="TVerifier">The type of verifier to use.</typeparam>
    public class Verifier<TAnalyzer, TCodeFix, TTest, TVerifier>
           where TAnalyzer : DiagnosticAnalyzer, new()
           where TCodeFix : CodeFixProvider, new()
           where TTest : Test<TVerifier>, new()
           where TVerifier : IVerifier, new()
    {
        /// <summary>
        /// Creates a <see cref="DiagnosticResult"/> representing an expected diagnostic for the <em>single</em>
        /// <see cref="DiagnosticDescriptor"/> supported by the analyzer.
        /// </summary>
        /// <returns>A <see cref="DiagnosticResult"/> initialized using the single descriptor supported by the analyzer.</returns>
        /// <exception cref="InvalidOperationException">
        /// <para>If the analyzer declares support for more than one diagnostic descriptor.</para>
        /// <para>-or-</para>
        /// <para>If the analyzer does not declare support for any diagnostic descriptors.</para>
        /// </exception>
        public static DiagnosticResult Diagnostic()
        {
            var analyzer = new TAnalyzer();
            try
            {
                return Diagnostic(analyzer.SupportedDiagnostics.Single());
            }
            catch (InvalidOperationException ex)
            {
                throw new InvalidOperationException(
                    $"'{nameof(Diagnostic)}()' can only be used when the analyzer has a single supported diagnostic. Use the '{nameof(Diagnostic)}(DiagnosticDescriptor)' overload to specify the descriptor from which to create the expected result.",
                    ex);
            }
        }

        /// <summary>
        /// Creates a <see cref="DiagnosticResult"/> representing an expected diagnostic for the <em>single</em>
        /// <see cref="DiagnosticDescriptor"/> with the specified ID supported by the analyzer.
        /// </summary>
        /// <param name="diagnosticId">The expected diagnostic ID.</param>
        /// <returns>A <see cref="DiagnosticResult"/> initialized using the single descriptor with the specified ID supported by the analyzer.</returns>
        /// <exception cref="InvalidOperationException">
        /// <para>If the analyzer declares support for more than one diagnostic descriptor with the specified ID.</para>
        /// <para>-or-</para>
        /// <para>If the analyzer does not declare support for any diagnostic descriptors with the specified ID.</para>
        /// </exception>
        public static DiagnosticResult Diagnostic(string diagnosticId)
        {
            var analyzer = new TAnalyzer();
            try
            {
                return Diagnostic(analyzer.SupportedDiagnostics.Single(i => i.Id == diagnosticId));
            }
            catch (InvalidOperationException ex)
            {
                throw new InvalidOperationException(
                    $"'{nameof(Diagnostic)}(string)' can only be used when the analyzer has a single supported diagnostic with the specified ID. Use the '{nameof(Diagnostic)}(DiagnosticDescriptor)' overload to specify the descriptor from which to create the expected result.",
                    ex);
            }
        }

        /// <summary>
        /// Creates a <see cref="DiagnosticResult"/> representing an expected diagnostic for the specified
        /// <paramref name="descriptor"/>.
        /// </summary>
        /// <param name="descriptor">The diagnostic descriptor.</param>
        /// <returns>A <see cref="DiagnosticResult"/> initialed using the specified <paramref name="descriptor"/>.</returns>
        public static DiagnosticResult Diagnostic(DiagnosticDescriptor descriptor)
            => new DiagnosticResult(descriptor);

        /// <summary>
        /// Verifies the analyzer produces the specified diagnostics for the given source text.
        /// </summary>
        /// <param name="source">The source text to test, which may include markup syntax.</param>
        /// <param name="expected">The expected diagnostics. These diagnostics are in addition to any diagnostics
        /// defined in markup.</param>
        /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
        public static Task VerifyAnalyzerAsync(string source, params DiagnosticResult[] expected)
        {
            var test = new TTest
            {
                TestCode = source,
            };

            test.ExpectedDiagnostics.AddRange(expected);
            return test.RunAsync(CancellationToken.None);
        }

        /// <summary>
        /// Verifies the analyzer provides diagnostics which, in combination with the code fix, produce the expected
        /// fixed code.
        /// </summary>
        /// <param name="source">The source text to test. Any diagnostics are defined in markup.</param>
        /// <param name="fixedSource">The expected fixed source text. Any remaining diagnostics are defined in markup.</param>
        /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
        public static Task VerifyCodeFixAsync(string source, string fixedSource)
            => VerifyCodeFixAsync(source, DiagnosticResult.EmptyDiagnosticResults, fixedSource);

        /// <summary>
        /// Verifies the analyzer provides diagnostics which, in combination with the code fix, produce the expected
        /// fixed code.
        /// </summary>
        /// <param name="source">The source text to test, which may include markup syntax.</param>
        /// <param name="expected">The expected diagnostic. This diagnostic is in addition to any diagnostics defined in
        /// markup.</param>
        /// <param name="fixedSource">The expected fixed source text. Any remaining diagnostics are defined in markup.</param>
        /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
        public static Task VerifyCodeFixAsync(string source, DiagnosticResult expected, string fixedSource)
            => VerifyCodeFixAsync(source, new[] { expected }, fixedSource);

        /// <summary>
        /// Verifies the analyzer provides diagnostics which, in combination with the code fix, produce the expected
        /// fixed code.
        /// </summary>
        /// <param name="source">The source text to test, which may include markup syntax.</param>
        /// <param name="expected">The expected diagnostics. These diagnostics are in addition to any diagnostics
        /// defined in markup.</param>
        /// <param name="fixedSource">The expected fixed source text. Any remaining diagnostics are defined in markup.</param>
        /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
        public static Task VerifyCodeFixAsync(string source, DiagnosticResult[] expected, string fixedSource)
        {
            var test = new TTest
            {
                TestCode = source,
                FixedCode = fixedSource,
            };

            test.ExpectedDiagnostics.AddRange(expected);
            return test.RunAsync(CancellationToken.None);
        }
    }
}
