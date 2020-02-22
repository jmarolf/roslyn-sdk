// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Formatting;
using Microsoft.CodeAnalysis.Host.Mef;
using Microsoft.CodeAnalysis.Options;
using Microsoft.CodeAnalysis.Simplification;
using Microsoft.CodeAnalysis.Text;
using Microsoft.VisualStudio.Composition;
using IComparer = System.Collections.IComparer;

namespace Microsoft.CodeAnalysis.Testing
{
    public abstract class Test<TVerifier>
        where TVerifier : IVerifier, new()
    {
        private static readonly Lazy<IExportProviderFactory> ExportProviderFactory;

        static Test()
        {
            ExportProviderFactory = new Lazy<IExportProviderFactory>(
                () =>
                {
                    var discovery = new AttributedPartDiscovery(Resolver.DefaultInstance, isNonPublicSupported: true);
                    var parts = Task.Run(() => discovery.CreatePartsAsync(MefHostServices.DefaultAssemblies)).GetAwaiter().GetResult();
                    var catalog = ComposableCatalog.Create(Resolver.DefaultInstance).AddParts(parts).WithDocumentTextDifferencingService();

                    var configuration = CompositionConfiguration.Create(catalog);
                    var runtimeComposition = RuntimeComposition.CreateRuntimeComposition(configuration);
                    return runtimeComposition.CreateExportProviderFactory();
                },
                LazyThreadSafetyMode.ExecutionAndPublication);
        }

        /// <summary>
        /// Gets the default verifier for the test.
        /// </summary>
        protected static TVerifier Verify { get; } = new TVerifier();

        /// <summary>
        /// Gets the prefix to apply to source files added without an explicit name.
        /// </summary>
        protected virtual string DefaultFilePathPrefix { get; } = "Test";

        /// <summary>
        /// Gets the name of the default project created for testing.
        /// </summary>
        protected virtual string DefaultTestProjectName { get; } = "TestProject";

        /// <summary>
        /// Gets the default full name of the first source file added for a test.
        /// </summary>
        protected virtual string DefaultFilePath => DefaultFilePathPrefix + 0 + "." + DefaultFileExt;

        /// <summary>
        /// Gets the default file extension to use for files added to the test without an explicit name.
        /// </summary>
        protected abstract string DefaultFileExt { get; }

        /// <summary>
        /// Gets the language name used for the test.
        /// </summary>
        /// <value>
        /// The language name used for the test.
        /// </value>
        public abstract string Language { get; }

        /// <summary>
        /// Sets the input source file for analyzer or code fix testing.
        /// </summary>
        /// <seealso cref="TestState"/>
        public string TestCode
        {
            set
            {
                if (value != null)
                {
                    TestState.Sources.Add(value);
                }
            }
        }

        /// <summary>
        /// Gets the list of diagnostics expected in the source(s) and/or additonal files.
        /// </summary>
        public List<DiagnosticResult> ExpectedDiagnostics => TestState.ExpectedDiagnostics;

        /// <summary>
        /// Gets or sets the behavior of compiler diagnostics in validation scenarios. The default value is
        /// <see cref="CompilerDiagnostics.Errors"/>.
        /// </summary>
        public CompilerDiagnostics CompilerDiagnostics { get; set; } = CompilerDiagnostics.Errors;

        /// <summary>
        /// Gets or sets options for the markup processor when markup is used for diagnostics. The default value is
        /// <see cref="MarkupOptions.None"/>.
        /// </summary>
        public MarkupOptions MarkupOptions { get; set; }

        public SolutionState TestState { get; }

        /// <summary>
        /// Gets the collection of inputs to provide to the XML documentation resolver.
        /// </summary>
        /// <remarks>
        /// <para>Files in this collection may be referenced via <c>&lt;include&gt;</c> elements in documentation
        /// comments.</para>
        /// </remarks>
        public Dictionary<string, string> XmlReferences { get; } = new Dictionary<string, string>();

        /// <summary>
        /// Gets or sets the test behaviors applying to this analyzer. The default value is
        /// <see cref="TestBehaviors.None"/>.
        /// </summary>
        public TestBehaviors TestBehaviors { get; set; }

        /// <summary>
        /// Gets a collection of diagnostics to explicitly disable in the <see cref="CompilationOptions"/> for projects.
        /// </summary>
        public List<string> DisabledDiagnostics { get; } = new List<string>();

        public ReferenceAssemblies ReferenceAssemblies { get; set; } = ReferenceAssemblies.Default;

        /// <summary>
        /// Gets a collection of transformation functions to apply to <see cref="Workspace.Options"/> during diagnostic
        /// or code fix test setup.
        /// </summary>
        public List<Func<OptionSet, OptionSet>> OptionsTransforms { get; } = new List<Func<OptionSet, OptionSet>>();

        /// <summary>
        /// Gets a collection of transformation functions to apply to a <see cref="Solution"/> during diagnostic or code
        /// fix test setup.
        /// </summary>
        public List<Func<Solution, ProjectId, Solution>> SolutionTransforms { get; } = new List<Func<Solution, ProjectId, Solution>>();

        /// <summary>
        /// Gets the default diagnostic to use during markup processing. By default, the <em>single</em> diagnostic of
        /// the first analyzer is used, and no default diagonostic is available if multiple diagnostics are provided by
        /// the analyzer. If <see cref="MarkupOptions.UseFirstDescriptor"/> is used, the first available diagnostic
        /// is used.
        /// </summary>
        /// <param name="analyzers">The analyzers to consider.</param>
        /// <returns>The default diagnostic to use during markup processing.</returns>
        protected internal virtual DiagnosticDescriptor? GetDefaultDiagnostic(DiagnosticAnalyzer[] analyzers)
        {
            if (analyzers.Length == 0)
            {
                return null;
            }

            if (MarkupOptions.HasFlag(MarkupOptions.UseFirstDescriptor))
            {
                foreach (var analyzer in analyzers)
                {
                    if (analyzer.SupportedDiagnostics.Any())
                    {
                        return analyzer.SupportedDiagnostics[0];
                    }
                }

                return null;
            }
            else if (analyzers[0].SupportedDiagnostics.Length == 1)
            {
                return analyzers[0].SupportedDiagnostics[0];
            }
            else
            {
                return null;
            }
        }

        protected string FormatVerifierMessage(ImmutableArray<DiagnosticAnalyzer> analyzers, Diagnostic actual, DiagnosticResult expected, string message)
        {
            return $"{message}{Environment.NewLine}" +
                $"{Environment.NewLine}" +
                $"Expected diagnostic:{Environment.NewLine}" +
                $"    {FormatDiagnostics(analyzers, DefaultFilePath, expected)}{Environment.NewLine}" +
                $"Actual diagnostic:{Environment.NewLine}" +
                $"    {FormatDiagnostics(analyzers, DefaultFilePath, actual)}{Environment.NewLine}";
        }

        /// <summary>
        /// General method that gets a collection of actual <see cref="Diagnostic"/>s found in the source after the
        /// analyzer is run, then verifies each of them.
        /// </summary>
        /// <param name="sources">An array of strings to create source documents from to run the analyzers on.</param>
        /// <param name="additionalFiles">Additional documents to include in the project.</param>
        /// <param name="additionalProjects">Additional projects to include in the solution.</param>
        /// <param name="additionalMetadataReferences">Additional metadata references to include in the project.</param>
        /// <param name="expected">A collection of <see cref="DiagnosticResult"/>s that should appear after the analyzer
        /// is run on the sources.</param>
        /// <param name="verifier">The verifier to use for test assertions.</param>
        /// <param name="cancellationToken">The <see cref="CancellationToken"/> that the task will observe.</param>
        /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
        protected async Task VerifyDiagnosticsAsync((string filename, SourceText content)[] sources, (string filename, SourceText content)[] additionalFiles, ProjectState[] additionalProjects, MetadataReference[] additionalMetadataReferences, DiagnosticResult[] expected, IVerifier verifier, CancellationToken cancellationToken)
        {
            var analyzers = GetDiagnosticAnalyzers().ToImmutableArray();
            VerifyDiagnosticResults(await GetSortedDiagnosticsAsync(sources, additionalFiles, additionalProjects, additionalMetadataReferences, analyzers, verifier, cancellationToken).ConfigureAwait(false), analyzers, expected, verifier);
            await VerifyGeneratedCodeDiagnosticsAsync(analyzers, sources, additionalFiles, additionalProjects, additionalMetadataReferences, expected, verifier, cancellationToken).ConfigureAwait(false);
            await VerifySuppressionDiagnosticsAsync(analyzers, sources, additionalFiles, additionalProjects, additionalMetadataReferences, expected, verifier, cancellationToken).ConfigureAwait(false);
        }

        private async Task VerifyGeneratedCodeDiagnosticsAsync(ImmutableArray<DiagnosticAnalyzer> analyzers, (string filename, SourceText content)[] sources, (string filename, SourceText content)[] additionalFiles, ProjectState[] additionalProjects, MetadataReference[] additionalMetadataReferences, DiagnosticResult[] expected, IVerifier verifier, CancellationToken cancellationToken)
        {
            if (TestBehaviors.HasFlag(TestBehaviors.SkipGeneratedCodeCheck))
            {
                return;
            }

            if (!expected.Any(x => IsSubjectToExclusion(x, sources)))
            {
                return;
            }

            // Diagnostics reported by the compiler and analyzer diagnostics which don't have a location will
            // still be reported. We also insert a new line at the beginning so we have to move all diagnostic
            // locations which have a specific position down by one line.
            var expectedResults = expected
                .Where(x => !IsSubjectToExclusion(x, sources))
                .Select(x => IsInSourceFile(x, sources) ? x.WithLineOffset(1) : x)
                .ToArray();

            var generatedCodeVerifier = verifier.PushContext("Verifying exclusions in <auto-generated> code");
            var commentPrefix = Language == LanguageNames.CSharp ? "//" : "'";
            VerifyDiagnosticResults(await GetSortedDiagnosticsAsync(sources.Select(x => (x.filename, x.content.Replace(new TextSpan(0, 0), $" {commentPrefix} <auto-generated>\r\n"))).ToArray(), additionalFiles, additionalProjects, additionalMetadataReferences, analyzers, generatedCodeVerifier, cancellationToken).ConfigureAwait(false), analyzers, expectedResults, generatedCodeVerifier);
        }

        private async Task VerifySuppressionDiagnosticsAsync(ImmutableArray<DiagnosticAnalyzer> analyzers, (string filename, SourceText content)[] sources, (string filename, SourceText content)[] additionalFiles, ProjectState[] additionalProjects, MetadataReference[] additionalMetadataReferences, DiagnosticResult[] expected, IVerifier verifier, CancellationToken cancellationToken)
        {
            if (TestBehaviors.HasFlag(TestBehaviors.SkipSuppressionCheck))
            {
                return;
            }

            if (!expected.Any(x => IsSubjectToExclusion(x, sources)))
            {
                return;
            }

            // Diagnostics reported by the compiler and analyzer diagnostics which don't have a location will
            // still be reported. We also insert a new line at the beginning so we have to move all diagnostic
            // locations which have a specific position down by one line.
            var expectedResults = expected
                .Where(x => !IsSuppressible(analyzers, x, sources))
                .Select(x => IsInSourceFile(x, sources) ? x.WithLineOffset(1) : x)
                .ToArray();

            var prefix = Language == LanguageNames.CSharp ? "#pragma warning disable" : "#Disable Warning";
            var suppressionVerifier = verifier.PushContext($"Verifying exclusions in '{prefix}' code");
            var suppressedDiagnostics = expected.Where(x => IsSubjectToExclusion(x, sources)).Select(x => x.Id).Distinct();
            var suppression = prefix + " " + string.Join(", ", suppressedDiagnostics);
            VerifyDiagnosticResults(await GetSortedDiagnosticsAsync(sources.Select(x => (x.filename, x.content.Replace(new TextSpan(0, 0), $"{suppression}\r\n"))).ToArray(), additionalFiles, additionalProjects, additionalMetadataReferences, analyzers, suppressionVerifier, cancellationToken).ConfigureAwait(false), analyzers, expectedResults, suppressionVerifier);
        }

        /// <summary>
        /// Checks each of the actual <see cref="Diagnostic"/>s found and compares them with the corresponding
        /// <see cref="DiagnosticResult"/> in the array of expected results. <see cref="Diagnostic"/>s are considered
        /// equal only if the <see cref="DiagnosticResult.Spans"/>, <see cref="DiagnosticResult.Id"/>,
        /// <see cref="DiagnosticResult.Severity"/>, and <see cref="DiagnosticResult.Message"/> of the
        /// <see cref="DiagnosticResult"/> match the actual <see cref="Diagnostic"/>.
        /// </summary>
        /// <param name="actualResults">The <see cref="Diagnostic"/>s found by the compiler after running the analyzer
        /// on the source code.</param>
        /// <param name="analyzers">The analyzers that have been run on the sources.</param>
        /// <param name="expectedResults">A collection of <see cref="DiagnosticResult"/>s describing the expected
        /// diagnostics for the sources.</param>
        /// <param name="verifier">The verifier to use for test assertions.</param>
        private void VerifyDiagnosticResults(IEnumerable<Diagnostic> actualResults, ImmutableArray<DiagnosticAnalyzer> analyzers, DiagnosticResult[] expectedResults, IVerifier verifier)
        {
            var matchedDiagnostics = MatchDiagnostics(actualResults.ToArray(), expectedResults);
            verifier.Equal(actualResults.Count(), matchedDiagnostics.Count(x => x.actual is object), $"{nameof(MatchDiagnostics)} failed to include all actual diagnostics in the result");
            verifier.Equal(expectedResults.Length, matchedDiagnostics.Count(x => x.expected is object), $"{nameof(MatchDiagnostics)} failed to include all expected diagnostics in the result");

            actualResults = matchedDiagnostics.Select(x => x.actual).WhereNotNull();
            expectedResults = matchedDiagnostics.Where(x => x.expected is object).Select(x => x.expected.GetValueOrDefault()).ToArray();

            var expectedCount = expectedResults.Length;
            var actualCount = actualResults.Count();

            var diagnosticsOutput = actualResults.Any() ? FormatDiagnostics(analyzers, DefaultFilePath, actualResults.ToArray()) : "    NONE.";
            var message = $"Mismatch between number of diagnostics returned, expected \"{expectedCount}\" actual \"{actualCount}\"\r\n\r\nDiagnostics:\r\n{diagnosticsOutput}\r\n";
            verifier.Equal(expectedCount, actualCount, message);

            for (var i = 0; i < expectedResults.Length; i++)
            {
                var actual = actualResults.ElementAt(i);
                var expected = expectedResults[i];

                if (!expected.HasLocation)
                {
                    message = FormatVerifierMessage(analyzers, actual, expected, "Expected a project diagnostic with no location:");
                    verifier.Equal(Location.None, actual.Location, message);
                }
                else
                {
                    VerifyDiagnosticLocation(analyzers, actual, expected, actual.Location, expected.Spans[0], verifier);
                    if (!expected.Options.HasFlag(DiagnosticOptions.IgnoreAdditionalLocations))
                    {
                        var additionalLocations = actual.AdditionalLocations.ToArray();

                        message = FormatVerifierMessage(analyzers, actual, expected, $"Expected {expected.Spans.Length - 1} additional locations but got {additionalLocations.Length} for Diagnostic:");
                        verifier.Equal(expected.Spans.Length - 1, additionalLocations.Length, message);

                        for (var j = 0; j < additionalLocations.Length; ++j)
                        {
                            VerifyDiagnosticLocation(analyzers, actual, expected, additionalLocations[j], expected.Spans[j + 1], verifier);
                        }
                    }
                }

                message = FormatVerifierMessage(analyzers, actual, expected, $"Expected diagnostic id to be \"{expected.Id}\" was \"{actual.Id}\"");
                verifier.Equal(expected.Id, actual.Id, message);

                if (!expected.Options.HasFlag(DiagnosticOptions.IgnoreSeverity))
                {
                    message = FormatVerifierMessage(analyzers, actual, expected, $"Expected diagnostic severity to be \"{expected.Severity}\" was \"{actual.Severity}\"");
                    verifier.Equal(expected.Severity, actual.Severity, message);
                }

                if (expected.Message != null)
                {
                    message = FormatVerifierMessage(analyzers, actual, expected, $"Expected diagnostic message to be \"{expected.Message}\" was \"{actual.GetMessage()}\"");
                    verifier.Equal(expected.Message, actual.GetMessage(), message);
                }
                else if (expected.MessageArguments?.Length > 0)
                {
                    message = FormatVerifierMessage(analyzers, actual, expected, $"Expected diagnostic message arguments to match");
                    verifier.SequenceEqual(
                        expected.MessageArguments.Select(argument => argument?.ToString() ?? string.Empty),
                        GetArguments(actual).Select(argument => argument?.ToString() ?? string.Empty),
                        StringComparer.Ordinal,
                        message);
                }
            }
        }

        /// <summary>
        /// Match actual diagnostics with expected diagnostics.
        /// </summary>
        /// <remarks>
        /// <para>While each actual diagnostic contains complete information about the diagnostic (location, severity,
        /// message, etc.), the expected diagnostics sometimes contain partial information. It is therefore possible for
        /// an expected diagnostic to match more than one actual diagnostic, while another expected diagnostic with more
        /// complete information only matches a single specific actual diagnostic.</para>
        ///
        /// <para>This method attempts to find a best matching of actual and expected diagnostics.</para>
        /// </remarks>
        /// <param name="actualResults">The actual diagnostics reported by analysis.</param>
        /// <param name="expectedResults">The expected diagnostics.</param>
        /// <returns>
        /// <para>A collection of matched diagnostics, with the following characteristics:</para>
        ///
        /// <list type="bullet">
        /// <item><description>Every element of <paramref name="actualResults"/> will appear exactly once as the first element of an item in the result.</description></item>
        /// <item><description>Every element of <paramref name="expectedResults"/> will appear exactly once as the second element of an item in the result.</description></item>
        /// <item><description>An item in the result which specifies both a <see cref="Diagnostic"/> and a <see cref="DiagnosticResult"/> indicates a matched pair, i.e. the actual and expected results are believed to refer to the same diagnostic.</description></item>
        /// <item><description>An item in the result which specifies only a <see cref="Diagnostic"/> indicates an actual diagnostic for which no matching expected diagnostic was found.</description></item>
        /// <item><description>An item in the result which specifies only a <see cref="DiagnosticResult"/> indicates an expected diagnostic for which no matching actual diagnostic was found.</description></item>
        ///
        /// <para>If no exact match is found (all actual diagnostics are matched to an expected diagnostic without
        /// errors), this method is <em>allowed</em> to attempt fall-back matching using a strategy intended to minimize
        /// the total number of mismatched pairs.</para>
        /// </list>
        /// </returns>
        private ImmutableArray<(Diagnostic? actual, DiagnosticResult? expected)> MatchDiagnostics(Diagnostic[] actualResults, DiagnosticResult[] expectedResults)
        {
            var actualIds = actualResults.Select(result => result.Id).ToImmutableArray();
            var actualResultLocations = actualResults.Select(result => (location: result.Location.GetLineSpan(), additionalLocations: result.AdditionalLocations.Select(location => location.GetLineSpan()).ToImmutableArray())).ToImmutableArray();
            var actualArguments = actualResults.Select(actual => GetArguments(actual).Select(argument => argument?.ToString() ?? string.Empty).ToImmutableArray()).ToImmutableArray();

            expectedResults = expectedResults.ToOrderedArray();
            var expectedArguments = expectedResults.Select(expected => expected.MessageArguments?.Select(argument => argument?.ToString() ?? string.Empty).ToImmutableArray() ?? ImmutableArray<string>.Empty).ToImmutableArray();

            // Initialize the best match to a trivial result where everything is unmatched. This will be updated if/when
            // better matches are found.
            var bestMatchCount = MatchQuality.RemainingUnmatched(actualResults.Length + expectedResults.Length);
            var bestMatch = actualResults.Select(result => ((Diagnostic?)result, default(DiagnosticResult?))).Concat(expectedResults.Select(result => (default(Diagnostic?), (DiagnosticResult?)result))).ToImmutableArray();

            var builder = ImmutableArray.CreateBuilder<(Diagnostic? actual, DiagnosticResult? expected)>();
            var usedExpected = new bool[expectedResults.Length];
            _ = RecursiveMatch(0, actualResults.Length, 0, expectedArguments.Length, MatchQuality.Full, usedExpected);

            return bestMatch;

            // Match items using recursive backtracking. Returns the distance the best match under this path is from an
            // ideal result of 0 (1:1 matching of actual and expected results). Currently the distance is calculated as
            // the sum of the match values:
            //
            // * Fully-matched items have a value of MatchQuality.Full.
            // * Partially-matched items have a value between MatchQuality.Full and MatchQuality.None (exclusive).
            // * Fully-unmatched items have a value of MatchQuality.None.
            MatchQuality RecursiveMatch(int firstActualIndex, int remainingActualItems, int firstExpectedIndex, int remainingExpectedItems, MatchQuality unmatchedActualResults, bool[] usedExpected)
            {
                var matchedOnEntry = actualResults.Length - remainingActualItems;
                var bestPossibleUnmatchedExpected = MatchQuality.RemainingUnmatched(Math.Abs(remainingActualItems - remainingExpectedItems));
                var bestPossible = unmatchedActualResults + bestPossibleUnmatchedExpected;

                if (firstActualIndex == actualResults.Length)
                {
                    // We reached the end of the actual diagnostics. Any remaning unmatched expected diagnostics should
                    // be added to the end. If this path produced a better result than the best known path so far,
                    // update the best match to this one.
                    var totalUnmatched = unmatchedActualResults + MatchQuality.RemainingUnmatched(remainingExpectedItems);

                    // Avoid manipulating the builder if we know the current path is no better than the previous best.
                    if (totalUnmatched < bestMatchCount)
                    {
                        var addedCount = 0;

                        // Add the remaining unmatched expected diagnostics
                        for (var i = firstExpectedIndex; i < expectedResults.Length; i++)
                        {
                            if (!usedExpected[i])
                            {
                                addedCount++;
                                builder.Add((null, (DiagnosticResult?)expectedResults[i]));
                            }
                        }

                        bestMatchCount = totalUnmatched;
                        bestMatch = builder.ToImmutable();

                        for (var i = 0; i < addedCount; i++)
                        {
                            builder.RemoveAt(builder.Count - 1);
                        }
                    }

                    return totalUnmatched;
                }

                var currentBest = unmatchedActualResults + MatchQuality.RemainingUnmatched(remainingActualItems + remainingExpectedItems);
                for (var i = firstExpectedIndex; i < expectedResults.Length; i++)
                {
                    if (usedExpected[i])
                    {
                        continue;
                    }

                    var (lineSpan, additionalLineSpans) = actualResultLocations[firstActualIndex];
                    var matchValue = GetMatchValue(actualResults[firstActualIndex], actualIds[firstActualIndex], lineSpan, additionalLineSpans, actualArguments[firstActualIndex], expectedResults[i], expectedArguments[i]);
                    if (matchValue == MatchQuality.None)
                    {
                        continue;
                    }

                    try
                    {
                        usedExpected[i] = true;
                        builder.Add((actualResults[firstActualIndex], expectedResults[i]));
                        var bestResultWithCurrentMatch = RecursiveMatch(firstActualIndex + 1, remainingActualItems - 1, i == firstExpectedIndex ? firstExpectedIndex + 1 : firstExpectedIndex, remainingExpectedItems - 1, unmatchedActualResults + matchValue, usedExpected);
                        currentBest = Min(bestResultWithCurrentMatch, currentBest);
                        if (currentBest == bestPossible)
                        {
                            // Return immediately if we know the current actual result cannot be paired with a different
                            // expected result to produce a better match.
                            return bestPossible;
                        }
                    }
                    finally
                    {
                        usedExpected[i] = false;
                        builder.RemoveAt(builder.Count - 1);
                    }
                }

                if (currentBest > unmatchedActualResults)
                {
                    // We might be able to improve the results by leaving the current actual diagnostic unmatched
                    try
                    {
                        builder.Add((actualResults[firstActualIndex], null));
                        var bestResultWithCurrentUnmatched = RecursiveMatch(firstActualIndex + 1, remainingActualItems - 1, firstExpectedIndex, remainingExpectedItems, unmatchedActualResults + MatchQuality.None, usedExpected);
                        return Min(bestResultWithCurrentUnmatched, currentBest);
                    }
                    finally
                    {
                        builder.RemoveAt(builder.Count - 1);
                    }
                }

                Debug.Assert(currentBest == unmatchedActualResults, $"Assertion failure: {currentBest} == {unmatchedActualResults}");
                return currentBest;
            }

            static MatchQuality Min(MatchQuality val1, MatchQuality val2)
                => val2 < val1 ? val2 : val1;

            static MatchQuality GetMatchValue(Diagnostic diagnostic, string diagnosticId, FileLinePositionSpan lineSpan, ImmutableArray<FileLinePositionSpan> additionalLineSpans, ImmutableArray<string> actualArguments, DiagnosticResult diagnosticResult, ImmutableArray<string> expectedArguments)
            {
                // A full match automatically gets the value MatchQuality.Full. A partial match gets a "point" for each
                // of the following elements:
                //
                // 1. Diagnostic span start
                // 2. Diagnostic span end
                // 3. Diagnostic ID
                //
                // A partial match starts at MatchQuality.None, with a point deduction for each of the above matching
                // items.
                var isLocationMatch = IsLocationMatch(diagnostic, lineSpan, additionalLineSpans, diagnosticResult, out var matchSpanStart, out var matchSpanEnd);
                var isIdMatch = diagnosticId == diagnosticResult.Id;
                if (isLocationMatch
                    && isIdMatch
                    && IsSeverityMatch(diagnostic, diagnosticResult)
                    && IsMessageMatch(diagnostic, actualArguments, diagnosticResult, expectedArguments))
                {
                    return MatchQuality.Full;
                }

                var points = (matchSpanStart ? 1 : 0) + (matchSpanEnd ? 1 : 0) + (isIdMatch ? 1 : 0);
                if (points == 0)
                {
                    return MatchQuality.None;
                }

                return new MatchQuality(4 - points);
            }

            static bool IsLocationMatch(Diagnostic diagnostic, FileLinePositionSpan lineSpan, ImmutableArray<FileLinePositionSpan> additionalLineSpans, DiagnosticResult diagnosticResult, out bool matchSpanStart, out bool matchSpanEnd)
            {
                if (!diagnosticResult.HasLocation)
                {
                    matchSpanStart = false;
                    matchSpanEnd = false;
                    return Equals(Location.None, diagnostic.Location);
                }
                else
                {
                    if (!IsLocationMatch2(diagnostic.Location, lineSpan, diagnosticResult.Spans[0], out matchSpanStart, out matchSpanEnd))
                    {
                        return false;
                    }

                    if (diagnosticResult.Options.HasFlag(DiagnosticOptions.IgnoreAdditionalLocations))
                    {
                        return true;
                    }

                    var additionalLocations = diagnostic.AdditionalLocations.ToArray();
                    if (additionalLocations.Length != diagnosticResult.Spans.Length - 1)
                    {
                        // Number of additional locations does not match expected result
                        return false;
                    }

                    for (var i = 0; i < additionalLocations.Length; i++)
                    {
                        if (!IsLocationMatch2(additionalLocations[i], additionalLineSpans[i], diagnosticResult.Spans[i + 1], out _, out _))
                        {
                            return false;
                        }
                    }

                    return true;
                }
            }

            static bool IsLocationMatch2(Location actual, FileLinePositionSpan actualSpan, DiagnosticLocation expected, out bool matchSpanStart, out bool matchSpanEnd)
            {
                matchSpanStart = actualSpan.StartLinePosition == expected.Span.StartLinePosition;
                matchSpanEnd = expected.Options.HasFlag(DiagnosticLocationOptions.IgnoreLength)
                    || actualSpan.EndLinePosition == expected.Span.EndLinePosition;

                var assert = actualSpan.Path == expected.Span.Path || (actualSpan.Path?.Contains("Test0.") == true && expected.Span.Path.Contains("Test."));
                if (!assert)
                {
                    // Expected diagnostic to be in file "{expected.Span.Path}" was actually in file "{actualSpan.Path}"
                    return false;
                }

                if (!matchSpanStart || !matchSpanEnd)
                {
                    return false;
                }

                return true;
            }

            static bool IsSeverityMatch(Diagnostic actual, DiagnosticResult expected)
            {
                if (expected.Options.HasFlag(DiagnosticOptions.IgnoreSeverity))
                {
                    return true;
                }

                return actual.Severity == expected.Severity;
            }

            static bool IsMessageMatch(Diagnostic actual, ImmutableArray<string> actualArguments, DiagnosticResult expected, ImmutableArray<string> expectedArguments)
            {
                if (expected.Message is null)
                {
                    if (expected.MessageArguments?.Length > 0)
                    {
                        return actualArguments.SequenceEqual(expectedArguments);
                    }

                    return true;
                }

                return string.Equals(expected.Message, actual.GetMessage());
            }
        }

        /// <summary>
        /// Helper method to <see cref="VerifyDiagnosticResults"/> that checks the location of a
        /// <see cref="Diagnostic"/> and compares it with the location described by a
        /// <see cref="FileLinePositionSpan"/>.
        /// </summary>
        /// <param name="analyzers">The analyzer that have been run on the sources.</param>
        /// <param name="diagnostic">The diagnostic that was found in the code.</param>
        /// <param name="expectedDiagnostic">The expected diagnostic.</param>
        /// <param name="actual">The location of the diagnostic found in the code.</param>
        /// <param name="expected">The <see cref="FileLinePositionSpan"/> describing the expected location of the
        /// diagnostic.</param>
        /// <param name="verifier">The verifier to use for test assertions.</param>
        private void VerifyDiagnosticLocation(ImmutableArray<DiagnosticAnalyzer> analyzers, Diagnostic diagnostic, DiagnosticResult expectedDiagnostic, Location actual, DiagnosticLocation expected, IVerifier verifier)
        {
            var actualSpan = actual.GetLineSpan();

            var assert = actualSpan.Path == expected.Span.Path || (actualSpan.Path?.Contains("Test0.") == true && expected.Span.Path.Contains("Test."));

            var message = FormatVerifierMessage(analyzers, diagnostic, expectedDiagnostic, $"Expected diagnostic to be in file \"{expected.Span.Path}\" was actually in file \"{actualSpan.Path}\"");
            verifier.True(assert, message);

            VerifyLinePosition(analyzers, diagnostic, expectedDiagnostic, actualSpan.StartLinePosition, expected.Span.StartLinePosition, "start", verifier);
            if (!expected.Options.HasFlag(DiagnosticLocationOptions.IgnoreLength))
            {
                VerifyLinePosition(analyzers, diagnostic, expectedDiagnostic, actualSpan.EndLinePosition, expected.Span.EndLinePosition, "end", verifier);
            }
        }

        private void VerifyLinePosition(ImmutableArray<DiagnosticAnalyzer> analyzers, Diagnostic diagnostic, DiagnosticResult expectedDiagnostic, LinePosition actualLinePosition, LinePosition expectedLinePosition, string positionText, IVerifier verifier)
        {
            var message = FormatVerifierMessage(analyzers, diagnostic, expectedDiagnostic, $"Expected diagnostic to {positionText} on line \"{expectedLinePosition.Line + 1}\" was actually on line \"{actualLinePosition.Line + 1}\"");
            verifier.Equal(
                expectedLinePosition.Line,
                actualLinePosition.Line,
                message);

            message = FormatVerifierMessage(analyzers, diagnostic, expectedDiagnostic, $"Expected diagnostic to {positionText} at column \"{expectedLinePosition.Character + 1}\" was actually at column \"{actualLinePosition.Character + 1}\"");
            verifier.Equal(
                expectedLinePosition.Character,
                actualLinePosition.Character,
                message);
        }

        /// <summary>
        /// Helper method to format a <see cref="Diagnostic"/> into an easily readable string.
        /// </summary>
        /// <param name="analyzers">The analyzers that this verifier tests.</param>
        /// <param name="defaultFilePath">The default file path for diagnostics.</param>
        /// <param name="diagnostics">A collection of <see cref="Diagnostic"/>s to be formatted.</param>
        /// <returns>The <paramref name="diagnostics"/> formatted as a string.</returns>
        private static string FormatDiagnostics(ImmutableArray<DiagnosticAnalyzer> analyzers, string defaultFilePath, params Diagnostic[] diagnostics)
        {
            var builder = new StringBuilder();
            for (var i = 0; i < diagnostics.Length; ++i)
            {
                var diagnosticsId = diagnostics[i].Id;
                var location = diagnostics[i].Location;

                builder.Append("// ").AppendLine(diagnostics[i].ToString());

                var applicableAnalyzer = analyzers.FirstOrDefault(a => a.SupportedDiagnostics.Any(dd => dd.Id == diagnosticsId));
                if (applicableAnalyzer != null)
                {
                    var analyzerType = applicableAnalyzer.GetType();
                    var rule = location != Location.None && location.IsInSource && applicableAnalyzer.SupportedDiagnostics.Length == 1 ? string.Empty : $"{analyzerType.Name}.{diagnosticsId}";

                    if (location == Location.None || !location.IsInSource)
                    {
                        builder.Append($"new DiagnosticResult({rule})");
                    }
                    else
                    {
                        var resultMethodName = location.SourceTree.FilePath.EndsWith(".cs") ? "VerifyCS.Diagnostic" : "VerifyVB.Diagnostic";
                        builder.Append($"{resultMethodName}({rule})");
                    }
                }
                else
                {
                    builder.Append(
                        diagnostics[i].Severity switch
                        {
                            DiagnosticSeverity.Error => $"{nameof(DiagnosticResult)}.{nameof(DiagnosticResult.CompilerError)}(\"{diagnostics[i].Id}\")",
                            DiagnosticSeverity.Warning => $"{nameof(DiagnosticResult)}.{nameof(DiagnosticResult.CompilerWarning)}(\"{diagnostics[i].Id}\")",
                            var severity => $"new {nameof(DiagnosticResult)}(\"{diagnostics[i].Id}\", {nameof(DiagnosticSeverity)}.{severity})",
                        });
                }

                if (location == Location.None)
                {
                    // No additional location data needed
                }
                else
                {
                    AppendLocation(diagnostics[i].Location);
                    foreach (var additionalLocation in diagnostics[i].AdditionalLocations)
                    {
                        AppendLocation(additionalLocation);
                    }
                }

                var arguments = GetArguments(diagnostics[i]);
                if (arguments.Count > 0)
                {
                    builder.Append($".{nameof(DiagnosticResult.WithArguments)}(");
                    builder.Append(string.Join(", ", arguments.Select(a => "\"" + a?.ToString() + "\"")));
                    builder.Append(")");
                }

                builder.AppendLine(",");
            }

            return builder.ToString();

            // Local functions
            void AppendLocation(Location location)
            {
                var lineSpan = location.GetLineSpan();
                var pathString = location.IsInSource && lineSpan.Path == defaultFilePath ? string.Empty : $"\"{lineSpan.Path}\", ";
                var linePosition = lineSpan.StartLinePosition;
                var endLinePosition = lineSpan.EndLinePosition;
                builder.Append($".WithSpan({pathString}{linePosition.Line + 1}, {linePosition.Character + 1}, {endLinePosition.Line + 1}, {endLinePosition.Character + 1})");
            }
        }

        /// <summary>
        /// Helper method to format a <see cref="Diagnostic"/> into an easily readable string.
        /// </summary>
        /// <param name="analyzers">The analyzers that this verifier tests.</param>
        /// <param name="defaultFilePath">The default file path for diagnostics.</param>
        /// <param name="diagnostics">A collection of <see cref="DiagnosticResult"/>s to be formatted.</param>
        /// <returns>The <paramref name="diagnostics"/> formatted as a string.</returns>
        private static string FormatDiagnostics(ImmutableArray<DiagnosticAnalyzer> analyzers, string defaultFilePath, params DiagnosticResult[] diagnostics)
        {
            var builder = new StringBuilder();
            for (var i = 0; i < diagnostics.Length; ++i)
            {
                var diagnosticsId = diagnostics[i].Id;

                builder.Append("// ").AppendLine(diagnostics[i].ToString());

                var applicableAnalyzer = analyzers.FirstOrDefault(a => a.SupportedDiagnostics.Any(dd => dd.Id == diagnosticsId));
                if (applicableAnalyzer != null)
                {
                    var analyzerType = applicableAnalyzer.GetType();
                    var rule = diagnostics[i].HasLocation && applicableAnalyzer.SupportedDiagnostics.Length == 1 ? string.Empty : $"{analyzerType.Name}.{diagnosticsId}";

                    if (!diagnostics[i].HasLocation)
                    {
                        builder.Append($"new DiagnosticResult({rule})");
                    }
                    else
                    {
                        var resultMethodName = diagnostics[i].Spans[0].Span.Path.EndsWith(".cs") ? "VerifyCS.Diagnostic" : "VerifyVB.Diagnostic";
                        builder.Append($"{resultMethodName}({rule})");
                    }
                }
                else
                {
                    builder.Append(
                        diagnostics[i].Severity switch
                        {
                            DiagnosticSeverity.Error => $"{nameof(DiagnosticResult)}.{nameof(DiagnosticResult.CompilerError)}(\"{diagnostics[i].Id}\")",
                            DiagnosticSeverity.Warning => $"{nameof(DiagnosticResult)}.{nameof(DiagnosticResult.CompilerWarning)}(\"{diagnostics[i].Id}\")",
                            var severity => $"new {nameof(DiagnosticResult)}(\"{diagnostics[i].Id}\", {nameof(DiagnosticSeverity)}.{severity})",
                        });
                }

                if (!diagnostics[i].HasLocation)
                {
                    // No additional location data needed
                }
                else
                {
                    foreach (var span in diagnostics[i].Spans)
                    {
                        AppendLocation(span);
                        if (diagnostics[i].Options.HasFlag(DiagnosticOptions.IgnoreAdditionalLocations))
                        {
                            break;
                        }
                    }
                }

                var arguments = diagnostics[i].MessageArguments;
                if (arguments?.Length > 0)
                {
                    builder.Append($".{nameof(DiagnosticResult.WithArguments)}(");
                    builder.Append(string.Join(", ", arguments.Select(a => "\"" + a?.ToString() + "\"")));
                    builder.Append(")");
                }

                builder.AppendLine(",");
            }

            return builder.ToString();

            // Local functions
            void AppendLocation(DiagnosticLocation location)
            {
                var pathString = location.Span.Path == defaultFilePath ? string.Empty : $"\"{location.Span.Path}\", ";
                var linePosition = location.Span.StartLinePosition;

                if (location.Options.HasFlag(DiagnosticLocationOptions.IgnoreLength))
                {
                    builder.Append($".WithLocation({pathString}{linePosition.Line + 1}, {linePosition.Character + 1})");
                }
                else
                {
                    var endLinePosition = location.Span.EndLinePosition;
                    builder.Append($".WithSpan({pathString}{linePosition.Line + 1}, {linePosition.Character + 1}, {endLinePosition.Line + 1}, {endLinePosition.Character + 1})");
                }
            }
        }

        private static bool IsSubjectToExclusion(DiagnosticResult result, (string filename, SourceText content)[] sources)
        {
            if (result.Id.StartsWith("CS", StringComparison.Ordinal)
                || result.Id.StartsWith("BC", StringComparison.Ordinal))
            {
                // This is a compiler diagnostic
                return false;
            }

            if (result.Id.StartsWith("AD", StringComparison.Ordinal))
            {
                // This diagnostic is reported by the analyzer infrastructure
                return false;
            }

            if (result.Spans.IsEmpty)
            {
                return false;
            }

            if (!IsInSourceFile(result, sources))
            {
                // This diagnostic is not reported in a source file
                return false;
            }

            return true;
        }

        private static bool IsSuppressible(ImmutableArray<DiagnosticAnalyzer> analyzers, DiagnosticResult result, (string filename, SourceText content)[] sources)
        {
            if (!IsSubjectToExclusion(result, sources))
            {
                return false;
            }

            foreach (var analyzer in analyzers)
            {
                foreach (var diagnostic in analyzer.SupportedDiagnostics)
                {
                    if (diagnostic.Id != result.Id)
                    {
                        continue;
                    }

                    if (diagnostic.CustomTags.Contains(WellKnownDiagnosticTags.NotConfigurable))
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        private static bool IsInSourceFile(DiagnosticResult result, (string filename, SourceText content)[] sources)
        {
            if (!result.HasLocation)
            {
                return false;
            }

            return sources.Any(source => source.filename.Equals(result.Spans[0].Span.Path));
        }

        /// <summary>
        /// Given classes in the form of strings, their language, and an <see cref="DiagnosticAnalyzer"/> to apply to
        /// it, return the <see cref="Diagnostic"/>s found in the string after converting it to a
        /// <see cref="Document"/>.
        /// </summary>
        /// <param name="sources">Classes in the form of strings.</param>
        /// <param name="additionalFiles">Additional documents to include in the project.</param>
        /// <param name="additionalProjects">Additional projects to include in the solution.</param>
        /// <param name="additionalMetadataReferences">Additional metadata references to include in the project.</param>
        /// <param name="analyzers">The analyzers to be run on the sources.</param>
        /// <param name="verifier">The verifier to use for test assertions.</param>
        /// <param name="cancellationToken">The <see cref="CancellationToken"/> that the task will observe.</param>
        /// <returns>A collection of <see cref="Diagnostic"/>s that surfaced in the source code, sorted by
        /// <see cref="Diagnostic.Location"/>.</returns>
        private async Task<ImmutableArray<Diagnostic>> GetSortedDiagnosticsAsync((string filename, SourceText content)[] sources, (string filename, SourceText content)[] additionalFiles, ProjectState[] additionalProjects, MetadataReference[] additionalMetadataReferences, ImmutableArray<DiagnosticAnalyzer> analyzers, IVerifier verifier, CancellationToken cancellationToken)
        {
            var solution = await GetSolutionAsync(sources, additionalFiles, additionalProjects, additionalMetadataReferences, verifier, cancellationToken);
            return await GetSortedDiagnosticsAsync(solution, analyzers, CompilerDiagnostics, cancellationToken);
        }

        /// <summary>
        /// Given an analyzer and a collection of documents to apply it to, run the analyzer and gather an array of
        /// diagnostics found. The returned diagnostics are then ordered by location in the source documents.
        /// </summary>
        /// <param name="solution">The <see cref="Solution"/> that the analyzer(s) will be run on.</param>
        /// <param name="analyzers">The analyzer to run on the documents.</param>
        /// <param name="compilerDiagnostics">The behavior of compiler diagnostics in validation scenarios.</param>
        /// <param name="cancellationToken">The <see cref="CancellationToken"/> that the task will observe.</param>
        /// <returns>A collection of <see cref="Diagnostic"/>s that surfaced in the source code, sorted by
        /// <see cref="Diagnostic.Location"/>.</returns>
        protected async Task<ImmutableArray<Diagnostic>> GetSortedDiagnosticsAsync(Solution solution, ImmutableArray<DiagnosticAnalyzer> analyzers, CompilerDiagnostics compilerDiagnostics, CancellationToken cancellationToken)
        {
            var diagnostics = ImmutableArray.CreateBuilder<Diagnostic>();
            foreach (var project in solution.Projects)
            {
                var compilation = await project.GetCompilationAsync(cancellationToken).ConfigureAwait(false);
                var compilationWithAnalyzers = compilation.WithAnalyzers(analyzers, GetAnalyzerOptions(project), cancellationToken);
                var allDiagnostics = await compilationWithAnalyzers.GetAllDiagnosticsAsync().ConfigureAwait(false);

                diagnostics.AddRange(allDiagnostics.Where(diagnostic => !IsCompilerDiagnostic(diagnostic) || IsCompilerDiagnosticIncluded(diagnostic, compilerDiagnostics)));
            }

            var results = SortDistinctDiagnostics(diagnostics);
            return results.ToImmutableArray();
        }

        private static bool IsCompilerDiagnostic(Diagnostic diagnostic)
        {
            return diagnostic.Descriptor.CustomTags.Contains(WellKnownDiagnosticTags.Compiler);
        }

        /// <summary>
        /// Gets the effective analyzer options for a project. The default implementation returns
        /// <see cref="Project.AnalyzerOptions"/>.
        /// </summary>
        /// <param name="project">The project.</param>
        /// <returns>The effective <see cref="AnalyzerOptions"/> for the project.</returns>
        protected virtual AnalyzerOptions GetAnalyzerOptions(Project project)
            => project.AnalyzerOptions;

        /// <summary>
        /// Given an array of strings as sources and a language, turn them into a <see cref="Project"/> and return the
        /// solution.
        /// </summary>
        /// <param name="sources">Classes in the form of strings.</param>
        /// <param name="additionalFiles">Additional documents to include in the project.</param>
        /// <param name="additionalProjects">Additional projects to include in the solution.</param>
        /// <param name="additionalMetadataReferences">Additional metadata references to include in the project.</param>
        /// <param name="verifier">The verifier to use for test assertions.</param>
        /// <param name="cancellationToken">The <see cref="CancellationToken"/> that the task will observe.</param>
        /// <returns>A solution containing a project with the specified sources and additional files.</returns>
        private async Task<Solution> GetSolutionAsync((string filename, SourceText content)[] sources, (string filename, SourceText content)[] additionalFiles, ProjectState[] additionalProjects, MetadataReference[] additionalMetadataReferences, IVerifier verifier, CancellationToken cancellationToken)
        {
            verifier.LanguageIsSupported(Language);

            var project = await CreateProjectAsync(sources, additionalFiles, additionalProjects, additionalMetadataReferences, Language, cancellationToken);
            var documents = project.Documents.ToArray();

            verifier.Equal(sources.Length, documents.Length, "Amount of sources did not match amount of Documents created");

            return project.Solution;
        }

        /// <summary>
        /// Create a project using the input strings as sources.
        /// </summary>
        /// <remarks>
        /// <para>This method first creates a <see cref="Project"/> by calling <see cref="CreateProjectImplAsync"/>, and then
        /// applies compilation options to the project by calling <see cref="ApplyCompilationOptions"/>.</para>
        /// </remarks>
        /// <param name="sources">Classes in the form of strings.</param>
        /// <param name="additionalFiles">Additional documents to include in the project.</param>
        /// <param name="additionalProjects">Additional projects to include in the solution.</param>
        /// <param name="additionalMetadataReferences">Additional metadata references to include in the project.</param>
        /// <param name="language">The language the source classes are in. Values may be taken from the
        /// <see cref="LanguageNames"/> class.</param>
        /// <param name="cancellationToken">The <see cref="CancellationToken"/> that the task will observe.</param>
        /// <returns>A <see cref="Project"/> created out of the <see cref="Document"/>s created from the source
        /// strings.</returns>
        protected async Task<Project> CreateProjectAsync((string filename, SourceText content)[] sources, (string filename, SourceText content)[] additionalFiles, ProjectState[] additionalProjects, MetadataReference[] additionalMetadataReferences, string language, CancellationToken cancellationToken)
        {
            var project = await CreateProjectImplAsync(sources, additionalFiles, additionalProjects, additionalMetadataReferences, language, cancellationToken);
            return ApplyCompilationOptions(project);
        }

        /// <summary>
        /// Create a project using the input strings as sources.
        /// </summary>
        /// <param name="sources">Classes in the form of strings.</param>
        /// <param name="additionalFiles">Additional documents to include in the project.</param>
        /// <param name="additionalProjects">Additional projects to include in the solution.</param>
        /// <param name="additionalMetadataReferences">Additional metadata references to include in the project.</param>
        /// <param name="language">The language the source classes are in. Values may be taken from the
        /// <see cref="LanguageNames"/> class.</param>
        /// <param name="cancellationToken">The <see cref="CancellationToken"/> that the task will observe.</param>
        /// <returns>A <see cref="Project"/> created out of the <see cref="Document"/>s created from the source
        /// strings.</returns>
        protected virtual async Task<Project> CreateProjectImplAsync((string filename, SourceText content)[] sources, (string filename, SourceText content)[] additionalFiles, ProjectState[] additionalProjects, MetadataReference[] additionalMetadataReferences, string language, CancellationToken cancellationToken)
        {
            var fileNamePrefix = DefaultFilePathPrefix;
            var fileExt = DefaultFileExt;

            var projectId = ProjectId.CreateNewId(debugName: DefaultTestProjectName);
            var solution = await CreateSolutionAsync(projectId, language, cancellationToken);

            foreach (var projectState in additionalProjects)
            {
                var additionalProjectId = ProjectId.CreateNewId(debugName: projectState.Name);
                solution = solution.AddProject(additionalProjectId, projectState.Name, projectState.AssemblyName, projectState.Language);

                for (var i = 0; i < projectState.Sources.Count; i++)
                {
                    (var newFileName, var source) = projectState.Sources[i];
                    var documentId = DocumentId.CreateNewId(additionalProjectId, debugName: newFileName);
                    solution = solution.AddDocument(documentId, newFileName, source);
                }

                solution = solution.AddProjectReference(projectId, new ProjectReference(additionalProjectId));
            }

            solution = solution.AddMetadataReferences(projectId, additionalMetadataReferences);

            for (var i = 0; i < sources.Length; i++)
            {
                (var newFileName, var source) = sources[i];
                var documentId = DocumentId.CreateNewId(projectId, debugName: newFileName);
                solution = solution.AddDocument(documentId, newFileName, source);
            }

            for (var i = 0; i < additionalFiles.Length; i++)
            {
                (var newFileName, var source) = additionalFiles[i];
                var documentId = DocumentId.CreateNewId(projectId, debugName: newFileName);
                solution = solution.AddAdditionalDocument(documentId, newFileName, source);
            }

            foreach (var transform in SolutionTransforms)
            {
                solution = transform(solution, projectId);
            }

            return solution.GetProject(projectId);
        }

        /// <summary>
        /// Creates a solution that will be used as parent for the sources that need to be checked.
        /// </summary>
        /// <param name="projectId">The project identifier to use.</param>
        /// <param name="language">The language for which the solution is being created.</param>
        /// <param name="cancellationToken">The <see cref="CancellationToken"/> that the task will observe.</param>
        /// <returns>The created solution.</returns>
        protected virtual async Task<Solution> CreateSolutionAsync(ProjectId projectId, string language, CancellationToken cancellationToken)
        {
            var compilationOptions = CreateCompilationOptions();

            var xmlReferenceResolver = new TestXmlReferenceResolver();
            foreach (var xmlReference in XmlReferences)
            {
                xmlReferenceResolver.XmlReferences.Add(xmlReference.Key, xmlReference.Value);
            }

            compilationOptions = compilationOptions
                .WithXmlReferenceResolver(xmlReferenceResolver)
                .WithAssemblyIdentityComparer(ReferenceAssemblies.AssemblyIdentityComparer);

            var workspace = CreateWorkspace();
            foreach (var transform in OptionsTransforms)
            {
                workspace.Options = transform(workspace.Options);
            }

            var solution = workspace
                .CurrentSolution
                .AddProject(projectId, DefaultTestProjectName, DefaultTestProjectName, language)
                .WithProjectCompilationOptions(projectId, compilationOptions);

            var metadataReferences = await ReferenceAssemblies.ResolveAsync(language, cancellationToken);
            solution = solution.AddMetadataReferences(projectId, metadataReferences);

            var parseOptions = solution.GetProject(projectId).ParseOptions;
            solution = solution.WithProjectParseOptions(projectId, parseOptions.WithDocumentationMode(DocumentationMode.Diagnose));

            return solution;
        }

        /// <summary>
        /// Applies compilation options to a project.
        /// </summary>
        /// <remarks>
        /// <para>The default implementation configures the project by enabling all supported diagnostics of analyzers
        /// included in <see cref="GetDiagnosticAnalyzers"/> as well as <c>AD0001</c>. After configuring these
        /// diagnostics, any diagnostic IDs indicated in <see cref="DisabledDiagnostics"/> are explicitly suppressed
        /// using <see cref="ReportDiagnostic.Suppress"/>.</para>
        /// </remarks>
        /// <param name="project">The project.</param>
        /// <returns>The modified project.</returns>
        protected virtual Project ApplyCompilationOptions(Project project)
        {
            var analyzers = GetDiagnosticAnalyzers();

            var supportedDiagnosticsSpecificOptions = new Dictionary<string, ReportDiagnostic>();
            foreach (var analyzer in analyzers)
            {
                foreach (var diagnostic in analyzer.SupportedDiagnostics)
                {
                    // make sure the analyzers we are testing are enabled
                    supportedDiagnosticsSpecificOptions[diagnostic.Id] = ReportDiagnostic.Default;
                }
            }

            // Report exceptions during the analysis process as errors
            supportedDiagnosticsSpecificOptions.Add("AD0001", ReportDiagnostic.Error);

            foreach (var id in DisabledDiagnostics)
            {
                supportedDiagnosticsSpecificOptions[id] = ReportDiagnostic.Suppress;
            }

            // update the project compilation options
            var modifiedSpecificDiagnosticOptions = supportedDiagnosticsSpecificOptions.ToImmutableDictionary().SetItems(project.CompilationOptions.SpecificDiagnosticOptions);
            var modifiedCompilationOptions = project.CompilationOptions.WithSpecificDiagnosticOptions(modifiedSpecificDiagnosticOptions);

            var solution = project.Solution.WithProjectCompilationOptions(project.Id, modifiedCompilationOptions);
            return solution.GetProject(project.Id);
        }

        public virtual AdhocWorkspace CreateWorkspace()
        {
            var exportProvider = ExportProviderFactory.Value.CreateExportProvider();
            var host = MefHostServices.Create(exportProvider.AsCompositionContext());
            return new AdhocWorkspace(host);
        }

        protected abstract CompilationOptions CreateCompilationOptions();

        /// <summary>
        /// Sort <see cref="Diagnostic"/>s by location in source document.
        /// </summary>
        /// <param name="diagnostics">A collection of <see cref="Diagnostic"/>s to be sorted.</param>
        /// <returns>A collection containing the input <paramref name="diagnostics"/>, sorted by
        /// <see cref="Diagnostic.Location"/> and <see cref="Diagnostic.Id"/>.</returns>
        private static Diagnostic[] SortDistinctDiagnostics(IEnumerable<Diagnostic> diagnostics)
        {
            return diagnostics
                .OrderBy(d => d.Location.GetLineSpan().Path, StringComparer.Ordinal)
                .ThenBy(d => d.Location.SourceSpan.Start)
                .ThenBy(d => d.Location.SourceSpan.End)
                .ThenBy(d => d.Id)
                .ThenBy(d => GetArguments(d), LexicographicComparer.Instance).ToArray();
        }

        private static IReadOnlyList<object?> GetArguments(Diagnostic diagnostic)
        {
            return (IReadOnlyList<object?>?)diagnostic.GetType().GetProperty("Arguments", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(diagnostic)
                ?? new object[0];
        }

        /// <summary>
        /// Gets the analyzers being tested.
        /// </summary>
        /// <returns>
        /// New instances of all the analyzers being tested.
        /// </returns>
        protected abstract IEnumerable<DiagnosticAnalyzer> GetDiagnosticAnalyzers();

        private sealed class LexicographicComparer : IComparer<IEnumerable<object?>>
        {
            public static LexicographicComparer Instance { get; } = new LexicographicComparer();

            public int Compare(IEnumerable<object?> x, IEnumerable<object?> y)
            {
                using var xe = x.GetEnumerator();
                using var ye = y.GetEnumerator();

                while (xe.MoveNext())
                {
                    if (!ye.MoveNext())
                    {
                        // y has fewer elements
                        return 1;
                    }

                    IComparer elementComparer = Comparer<object>.Default;
                    if (xe.Current is string && ye.Current is string)
                    {
                        // Avoid culture-sensitive string comparisons
                        elementComparer = StringComparer.Ordinal;
                    }

                    try
                    {
                        var elementComparison = elementComparer.Compare(xe.Current, ye.Current);
                        if (elementComparison == 0)
                        {
                            continue;
                        }

                        return elementComparison;
                    }
                    catch (ArgumentException)
                    {
                        // The arguments are not directly comparable, so convert the values to strings and try again
                        var elementComparison = string.CompareOrdinal(xe.Current?.ToString(), ye.Current?.ToString());
                        if (elementComparison == 0)
                        {
                            continue;
                        }

                        return elementComparison;
                    }
                }

                if (ye.MoveNext())
                {
                    // x has fewer elements
                    return -1;
                }

                return 0;
            }
        }

        /// <summary>
        /// Gets or sets the index of the code fix to apply.
        /// </summary>
        /// <remarks>
        /// <para>If <see cref="CodeFixIndex"/> and <see cref="CodeFixEquivalenceKey"/> are both specified, the code fix
        /// test will further verify that the two properties refer to the same code fix.</para>
        /// </remarks>
        /// <seealso cref="CodeFixEquivalenceKey"/>
        public int? CodeFixIndex { get; set; }

        /// <summary>
        /// Gets or sets the <see cref="CodeAction.EquivalenceKey"/> of the code fix to apply.
        /// </summary>
        /// <remarks>
        /// <para>If <see cref="CodeFixIndex"/> and <see cref="CodeFixEquivalenceKey"/> are both specified, the code fix
        /// test will further verify that the two properties refer to the same code fix.</para>
        /// </remarks>
        /// <seealso cref="CodeFixIndex"/>
        public string? CodeFixEquivalenceKey { get; set; }

        /// <summary>
        /// Sets the expected output source file for code fix testing.
        /// </summary>
        /// <seealso cref="FixedState"/>
        public string FixedCode
        {
            set
            {
                if (value != null)
                {
                    FixedState.Sources.Add(value);
                }
            }
        }

        public SolutionState FixedState { get; }

        /// <summary>
        /// Sets the expected output source file after a Fix All operation is applied.
        /// </summary>
        /// <seealso cref="BatchFixedState"/>
        public string BatchFixedCode
        {
            set
            {
                if (value != null)
                {
                    BatchFixedState.Sources.Add(value);
                }
            }
        }

        public SolutionState BatchFixedState { get; }

        /// <summary>
        /// Gets or sets the number of code fix iterations expected during code fix testing.
        /// </summary>
        /// <remarks>
        /// <para>Code fixes are applied until one of the following conditions are met:</para>
        ///
        /// <list type="bullet">
        /// <item><description>No diagnostics are reported in the input.</description></item>
        /// <item><description>No code fixes are provided for the diagnostics reported in the input.</description></item>
        /// <item><description>The code fix applied for the diagnostics does not produce a change in the source file(s).</description></item>
        /// <item><description>The maximum number of allowed iterations is exceeded.</description></item>
        /// </list>
        ///
        /// <para>If the number of iterations is positive, it represents an exact number of iterations: code fix tests
        /// will fail if the code fix required more or fewer iterations to complete. If the number of iterations is
        /// negative, the negation of the number of iterations is treated as an upper bound on the number of allowed
        /// iterations: code fix tests will fail only if the code fix required more iterations to complete. If the
        /// number of iterations is zero, the code fix test will validate that no code fixes are offered for the set of
        /// diagnostics reported in the original input.</para>
        ///
        /// <para>When the number of iterations is not specified, the value is automatically selected according to the
        /// current test configuration:</para>
        ///
        /// <list type="bullet">
        /// <item><description>If the expected code fix output equals the input sources, the default value is treated as <c>0</c>.</description></item>
        /// <item><description>Otherwise, the default value is treated as the negative of the number of fixable diagnostics appearing in the input source file(s).</description></item>
        /// </list>
        ///
        /// <note>
        /// <para>The default value for this property can be interpreted as "Iterative code fix operations are expected
        /// to complete in at most one operation for each fixable diagnostic in the input source has been applied.
        /// Completing in fewer iterations is acceptable."</para>
        /// </note>
        /// </remarks>
        public int? NumberOfIncrementalIterations { get; set; }

        /// <summary>
        /// Gets or sets the number of code fix iterations expected during code fix testing for Fix All scenarios.
        /// </summary>
        /// <remarks>
        /// <para>See the <see cref="NumberOfIncrementalIterations"/> property for an overview of the behavior of this
        /// property. If the number of Fix All iterations is not specified, the value is automatically selected
        /// according to the current test configuration:</para>
        ///
        /// <list type="bullet">
        /// <item><description>If the expected Fix All output equals the input sources, the default value is treated as <c>0</c>.</description></item>
        /// <item><description>Otherwise, the default value is treated as <c>1</c>.</description></item>
        /// </list>
        ///
        /// <note>
        /// <para>The default value for this property can be interpreted as "Fix All operations are expected to complete
        /// in the minimum number of iterations possible unless otherwise specified."</para>
        /// </note>
        /// </remarks>
        /// <seealso cref="NumberOfIncrementalIterations"/>
        public int? NumberOfFixAllIterations { get; set; }

        /// <summary>
        /// Gets or sets the number of code fix iterations expected during code fix testing for Fix All in Document
        /// scenarios.
        /// </summary>
        /// <remarks>
        /// <para>See the <see cref="NumberOfIncrementalIterations"/> property for an overview of the behavior of this
        /// property. If the number of Fix All in Document iterations is not specified, the value from
        /// <see cref="NumberOfFixAllIterations"/> is used.</para>
        /// </remarks>
        /// <seealso cref="NumberOfIncrementalIterations"/>
        /// <seealso cref="NumberOfFixAllIterations"/>
        /// <seealso href="https://github.com/dotnet/roslyn-sdk/issues/147">#147: Figure out Fix All iteration counts by context</seealso>
        public int? NumberOfFixAllInDocumentIterations { get; set; }

        /// <summary>
        /// Gets or sets the validation mode for code fixes. The default is
        /// <see cref="CodeFixValidationMode.SemanticStructure"/>.
        /// </summary>
        public CodeFixValidationMode CodeFixValidationMode { get; set; } = CodeFixValidationMode.SemanticStructure;

        /// <summary>
        /// Gets the syntax kind enumeration type for the current code fix test.
        /// </summary>
        public abstract Type SyntaxKindType { get; }

        protected Test()
        {
            TestState = new SolutionState(DefaultFilePathPrefix, DefaultFileExt);
            FixedState = new SolutionState(DefaultFilePathPrefix, DefaultFileExt);
            BatchFixedState = new SolutionState(DefaultFilePathPrefix, DefaultFileExt);
        }

        /// <summary>
        /// Returns the code fixes being tested - to be implemented in non-abstract class.
        /// </summary>
        /// <returns>The <see cref="CodeFixProvider"/> to be used.</returns>
        protected abstract IEnumerable<CodeFixProvider> GetCodeFixProviders();

        /// <summary>
        /// Determines if a compiler diagnostic should be included for diagnostic validation. The default implementation includes all diagnostics at a severity level indicated by <paramref name="compilerDiagnostics"/>.
        /// </summary>
        /// <param name="diagnostic">The compiler diagnostic.</param>
        /// <param name="compilerDiagnostics">The compiler diagnostic level in effect for the test.</param>
        /// <returns><see langword="true"/> to include the diagnostic for validation; otherwise, <see langword="false"/> to exclude a diagnostic.</returns>
        protected virtual bool IsCompilerDiagnosticIncluded(Diagnostic diagnostic, CompilerDiagnostics compilerDiagnostics)
        {
            return compilerDiagnostics switch
            {
                CompilerDiagnostics.Errors => diagnostic.Severity >= DiagnosticSeverity.Error,
                CompilerDiagnostics.Warnings => diagnostic.Severity >= DiagnosticSeverity.Warning,
                CompilerDiagnostics.Suggestions => diagnostic.Severity >= DiagnosticSeverity.Info,
                CompilerDiagnostics.All => CodeFixProvidersHandleDiagnostic(diagnostic),
                _ => CodeFixProvidersHandleDiagnostic(diagnostic),
            };

            bool CodeFixProvidersHandleDiagnostic(Diagnostic localDiagnostic)
            {
                var codeFixProviders = GetCodeFixProviders();
                return codeFixProviders
                    .Any(provider => provider.FixableDiagnosticIds.Any(fixerDiagnosticId => string.Equals(fixerDiagnosticId, localDiagnostic.Id, StringComparison.OrdinalIgnoreCase)));
            }
        }

        /// <summary>
        /// Runs the test.
        /// </summary>
        /// <param name="cancellationToken">The <see cref="CancellationToken"/> that the operation will observe.</param>
        /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
        public async Task RunAsync(CancellationToken cancellationToken = default)
        {
            Verify.NotEmpty($"{nameof(TestState)}.{nameof(SolutionState.Sources)}", TestState.Sources);

            var analyzers = GetDiagnosticAnalyzers().ToArray();
            var defaultDiagnostic = GetDefaultDiagnostic(analyzers);
            var supportedDiagnostics = analyzers.SelectMany(analyzer => analyzer.SupportedDiagnostics).ToImmutableArray();
            var fixableDiagnostics = GetCodeFixProviders().SelectMany(provider => provider.FixableDiagnosticIds).ToImmutableArray();

            var rawTestState = TestState.WithInheritedValuesApplied(null, fixableDiagnostics);
            var rawFixedState = FixedState.WithInheritedValuesApplied(rawTestState, fixableDiagnostics);
            var rawBatchFixedState = BatchFixedState.WithInheritedValuesApplied(rawFixedState, fixableDiagnostics);

            var testState = rawTestState.WithProcessedMarkup(MarkupOptions, defaultDiagnostic, supportedDiagnostics, fixableDiagnostics, DefaultFilePath);
            var fixedState = rawFixedState.WithProcessedMarkup(MarkupOptions, defaultDiagnostic, supportedDiagnostics, fixableDiagnostics, DefaultFilePath);
            var batchFixedState = rawBatchFixedState.WithProcessedMarkup(MarkupOptions, defaultDiagnostic, supportedDiagnostics, fixableDiagnostics, DefaultFilePath);

            await VerifyDiagnosticsAsync(testState.Sources.ToArray(), testState.AdditionalFiles.ToArray(), testState.AdditionalProjects.ToArray(), testState.AdditionalReferences.ToArray(), testState.ExpectedDiagnostics.ToArray(), Verify.PushContext("Diagnostics of test state"), cancellationToken).ConfigureAwait(false);

            if (CodeFixExpected())
            {
                await VerifyDiagnosticsAsync(fixedState.Sources.ToArray(), fixedState.AdditionalFiles.ToArray(), fixedState.AdditionalProjects.ToArray(), fixedState.AdditionalReferences.ToArray(), fixedState.ExpectedDiagnostics.ToArray(), Verify.PushContext("Diagnostics of fixed state"), cancellationToken).ConfigureAwait(false);
                if (CodeFixExpected(BatchFixedState))
                {
                    await VerifyDiagnosticsAsync(batchFixedState.Sources.ToArray(), batchFixedState.AdditionalFiles.ToArray(), batchFixedState.AdditionalProjects.ToArray(), batchFixedState.AdditionalReferences.ToArray(), batchFixedState.ExpectedDiagnostics.ToArray(), Verify.PushContext("Diagnostics of batch fixed state"), cancellationToken).ConfigureAwait(false);
                }

                await VerifyFixAsync(testState, fixedState, batchFixedState, Verify, cancellationToken).ConfigureAwait(false);
            }
        }

        private bool CodeFixExpected()
        {
            return CodeFixExpected(FixedState)
                || CodeFixExpected(BatchFixedState);
        }

        private static bool CodeFixExpected(SolutionState state)
        {
            return state.InheritanceMode != null
                || state.MarkupHandling != null
                || state.Sources.Any()
                || state.AdditionalFiles.Any()
                || state.AdditionalFilesFactories.Any();
        }

        /// <summary>
        /// Called to test a C# code fix when applied on the input source as a string.
        /// </summary>
        /// <param name="testState">The effective input test state.</param>
        /// <param name="fixedState">The effective test state after incremental code fixes are applied.</param>
        /// <param name="batchFixedState">The effective test state after batch code fixes are applied.</param>
        /// <param name="verifier">The verifier to use for test assertions.</param>
        /// <param name="cancellationToken">The <see cref="CancellationToken"/> that the task will observe.</param>
        /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
        protected async Task VerifyFixAsync(SolutionState testState, SolutionState fixedState, SolutionState batchFixedState, IVerifier verifier, CancellationToken cancellationToken)
        {
            int numberOfIncrementalIterations;
            int numberOfFixAllIterations;
            int numberOfFixAllInDocumentIterations;
            if (NumberOfIncrementalIterations != null)
            {
                numberOfIncrementalIterations = NumberOfIncrementalIterations.Value;
            }
            else
            {
                if (!HasAnyChange(testState, fixedState))
                {
                    numberOfIncrementalIterations = 0;
                }
                else
                {
                    // Expect at most one iteration per fixable diagnostic
                    var fixers = GetCodeFixProviders().ToArray();
                    var fixableExpectedDiagnostics = testState.ExpectedDiagnostics.Count(diagnostic => fixers.Any(fixer => fixer.FixableDiagnosticIds.Contains(diagnostic.Id)));
                    numberOfIncrementalIterations = -fixableExpectedDiagnostics;
                }
            }

            if (NumberOfFixAllIterations != null)
            {
                numberOfFixAllIterations = NumberOfFixAllIterations.Value;
            }
            else
            {
                if (!HasAnyChange(testState, batchFixedState))
                {
                    numberOfFixAllIterations = 0;
                }
                else
                {
                    numberOfFixAllIterations = 1;
                }
            }

            if (NumberOfFixAllInDocumentIterations != null)
            {
                numberOfFixAllInDocumentIterations = NumberOfFixAllInDocumentIterations.Value;
            }
            else
            {
                numberOfFixAllInDocumentIterations = numberOfFixAllIterations;
            }

            var t1 = VerifyFixAsync(Language, GetDiagnosticAnalyzers().ToImmutableArray(), GetCodeFixProviders().ToImmutableArray(), testState, fixedState, numberOfIncrementalIterations, FixEachAnalyzerDiagnosticAsync, verifier.PushContext("Iterative code fix application"), cancellationToken).ConfigureAwait(false);

            var fixAllProvider = GetCodeFixProviders().Select(codeFixProvider => codeFixProvider.GetFixAllProvider()).Where(codeFixProvider => codeFixProvider != null).ToImmutableArray();

            if (fixAllProvider.IsEmpty)
            {
                await t1;
            }
            else
            {
                if (Debugger.IsAttached)
                {
                    await t1;
                }

                var t2 = VerifyFixAsync(Language, GetDiagnosticAnalyzers().ToImmutableArray(), GetCodeFixProviders().ToImmutableArray(), testState, batchFixedState, numberOfFixAllInDocumentIterations, FixAllAnalyzerDiagnosticsInDocumentAsync, verifier.PushContext("Fix all in document"), cancellationToken).ConfigureAwait(false);
                if (Debugger.IsAttached)
                {
                    await t2;
                }

                var t3 = VerifyFixAsync(Language, GetDiagnosticAnalyzers().ToImmutableArray(), GetCodeFixProviders().ToImmutableArray(), testState, batchFixedState, numberOfFixAllIterations, FixAllAnalyzerDiagnosticsInProjectAsync, verifier.PushContext("Fix all in project"), cancellationToken).ConfigureAwait(false);
                if (Debugger.IsAttached)
                {
                    await t3;
                }

                var t4 = VerifyFixAsync(Language, GetDiagnosticAnalyzers().ToImmutableArray(), GetCodeFixProviders().ToImmutableArray(), testState, batchFixedState, numberOfFixAllIterations, FixAllAnalyzerDiagnosticsInSolutionAsync, verifier.PushContext("Fix all in solution"), cancellationToken).ConfigureAwait(false);
                if (Debugger.IsAttached)
                {
                    await t4;
                }

                if (!Debugger.IsAttached)
                {
                    // Allow the operations to run in parallel
                    await t1;
                    await t2;
                    await t3;
                    await t4;
                }
            }
        }

        private async Task VerifyFixAsync(
            string language,
            ImmutableArray<DiagnosticAnalyzer> analyzers,
            ImmutableArray<CodeFixProvider> codeFixProviders,
            SolutionState oldState,
            SolutionState newState,
            int numberOfIterations,
            Func<ImmutableArray<DiagnosticAnalyzer>, ImmutableArray<CodeFixProvider>, int?, string?, Project, int, IVerifier, CancellationToken, Task<(Project project, ExceptionDispatchInfo? iterationCountFailure)>> getFixedProject,
            IVerifier verifier,
            CancellationToken cancellationToken)
        {
            var project = await CreateProjectAsync(oldState.Sources.ToArray(), oldState.AdditionalFiles.ToArray(), oldState.AdditionalProjects.ToArray(), oldState.AdditionalReferences.ToArray(), language, cancellationToken);
            var compilerDiagnostics = await GetCompilerDiagnosticsAsync(project, cancellationToken).ConfigureAwait(false);

            ExceptionDispatchInfo? iterationCountFailure;
            (project, iterationCountFailure) = await getFixedProject(analyzers, codeFixProviders, CodeFixIndex, CodeFixEquivalenceKey, project, numberOfIterations, verifier, cancellationToken).ConfigureAwait(false);

            // After applying all of the code fixes, compare the resulting string to the inputted one
            var updatedDocuments = project.Documents.ToArray();

            verifier.Equal(newState.Sources.Count, updatedDocuments.Length, $"expected '{nameof(newState)}.{nameof(SolutionState.Sources)}' and '{nameof(updatedDocuments)}' to be equal but '{nameof(newState)}.{nameof(SolutionState.Sources)}' contains '{newState.Sources.Count}' documents and '{nameof(updatedDocuments)}' contains '{updatedDocuments.Length}' documents");

            for (var i = 0; i < updatedDocuments.Length; i++)
            {
                var actual = await GetSourceTextFromDocumentAsync(updatedDocuments[i], cancellationToken).ConfigureAwait(false);
                verifier.EqualOrDiff(newState.Sources[i].content.ToString(), actual.ToString(), $"content of '{newState.Sources[i].filename}' did not match. Diff shown with expected as baseline:");
                verifier.Equal(newState.Sources[i].content.Encoding, actual.Encoding, $"encoding of '{newState.Sources[i].filename}' was expected to be '{newState.Sources[i].content.Encoding}' but was '{actual.Encoding}'");
                verifier.Equal(newState.Sources[i].content.ChecksumAlgorithm, actual.ChecksumAlgorithm, $"checksum algorithm of '{newState.Sources[i].filename}' was expected to be '{newState.Sources[i].content.ChecksumAlgorithm}' but was '{actual.ChecksumAlgorithm}'");
                verifier.Equal(newState.Sources[i].filename, updatedDocuments[i].Name, $"file name was expected to be '{newState.Sources[i].filename}' but was '{updatedDocuments[i].Name}'");
            }

            var updatedAdditionalDocuments = project.AdditionalDocuments.ToArray();

            verifier.Equal(newState.AdditionalFiles.Count, updatedAdditionalDocuments.Length, $"expected '{nameof(newState)}.{nameof(SolutionState.AdditionalFiles)}' and '{nameof(updatedAdditionalDocuments)}' to be equal but '{nameof(newState)}.{nameof(SolutionState.AdditionalFiles)}' contains '{newState.AdditionalFiles.Count}' documents and '{nameof(updatedAdditionalDocuments)}' contains '{updatedAdditionalDocuments.Length}' documents");

            for (var i = 0; i < updatedAdditionalDocuments.Length; i++)
            {
                var actual = await updatedAdditionalDocuments[i].GetTextAsync(cancellationToken).ConfigureAwait(false);
                verifier.EqualOrDiff(newState.AdditionalFiles[i].content.ToString(), actual.ToString(), $"content of '{newState.AdditionalFiles[i].filename}' did not match. Diff shown with expected as baseline:");
                verifier.Equal(newState.AdditionalFiles[i].content.Encoding, actual.Encoding, $"encoding of '{newState.AdditionalFiles[i].filename}' was expected to be '{newState.AdditionalFiles[i].content.Encoding}' but was '{actual.Encoding}'");
                verifier.Equal(newState.AdditionalFiles[i].content.ChecksumAlgorithm, actual.ChecksumAlgorithm, $"checksum algorithm of '{newState.AdditionalFiles[i].filename}' was expected to be '{newState.AdditionalFiles[i].content.ChecksumAlgorithm}' but was '{actual.ChecksumAlgorithm}'");
                verifier.Equal(newState.AdditionalFiles[i].filename, updatedAdditionalDocuments[i].Name, $"file name was expected to be '{newState.AdditionalFiles[i].filename}' but was '{updatedAdditionalDocuments[i].Name}'");
            }

            // Validate the iteration counts after validating the content
            iterationCountFailure?.Throw();
        }

        private static bool HasAnyChange(SolutionState oldState, SolutionState newState)
        {
            return !oldState.Sources.SequenceEqual(newState.Sources, SourceFileEqualityComparer.Instance)
                || !oldState.AdditionalFiles.SequenceEqual(newState.AdditionalFiles, SourceFileEqualityComparer.Instance);
        }

        private async Task<(Project project, ExceptionDispatchInfo? iterationCountFailure)> FixEachAnalyzerDiagnosticAsync(ImmutableArray<DiagnosticAnalyzer> analyzers, ImmutableArray<CodeFixProvider> codeFixProviders, int? codeFixIndex, string? codeFixEquivalenceKey, Project project, int numberOfIterations, IVerifier verifier, CancellationToken cancellationToken)
        {
            var expectedNumberOfIterations = numberOfIterations;
            if (numberOfIterations < 0)
            {
                numberOfIterations = -numberOfIterations;
            }

            var previousDiagnostics = ImmutableArray.Create<Diagnostic>();

            bool done;
            do
            {
                var analyzerDiagnostics = await GetSortedDiagnosticsAsync(project.Solution, analyzers, CompilerDiagnostics, cancellationToken).ConfigureAwait(false);
                if (analyzerDiagnostics.Length == 0)
                {
                    break;
                }

                if (!AreDiagnosticsDifferent(analyzerDiagnostics, previousDiagnostics))
                {
                    break;
                }

                try
                {
                    verifier.True(--numberOfIterations >= -1, "The upper limit for the number of code fix iterations was exceeded");
                }
                catch (Exception ex)
                {
                    return (project, ExceptionDispatchInfo.Capture(ex));
                }

                previousDiagnostics = analyzerDiagnostics;

                done = true;
                var anyActions = false;
                foreach (var diagnostic in analyzerDiagnostics)
                {
                    var actions = new List<CodeAction>();

                    foreach (var codeFixProvider in codeFixProviders)
                    {
                        if (!codeFixProvider.FixableDiagnosticIds.Contains(diagnostic.Id))
                        {
                            // do not pass unsupported diagnostics to a code fix provider
                            continue;
                        }

                        var context = new CodeFixContext(project.GetDocument(diagnostic.Location.SourceTree), diagnostic, (a, d) => actions.Add(a), cancellationToken);
                        await codeFixProvider.RegisterCodeFixesAsync(context).ConfigureAwait(false);
                    }

                    var actionToApply = TryGetCodeActionToApply(actions, codeFixIndex, codeFixEquivalenceKey, verifier);
                    if (actionToApply != null)
                    {
                        anyActions = true;

                        var fixedProject = await ApplyFixAsync(project, actionToApply, cancellationToken).ConfigureAwait(false);
                        if (fixedProject != project)
                        {
                            done = false;

                            project = await RecreateProjectDocumentsAsync(fixedProject, verifier, cancellationToken).ConfigureAwait(false);
                            break;
                        }
                    }
                }

                if (!anyActions)
                {
                    verifier.True(done, "Expected to be done executing actions.");

                    // Avoid counting iterations that do not provide any code actions
                    numberOfIterations++;
                }
            }
            while (!done);

            try
            {
                if (expectedNumberOfIterations >= 0)
                {
                    verifier.Equal(expectedNumberOfIterations, expectedNumberOfIterations - numberOfIterations, $"Expected '{expectedNumberOfIterations}' iterations but found '{expectedNumberOfIterations - numberOfIterations}' iterations.");
                }
                else
                {
                    verifier.True(numberOfIterations >= 0, "The upper limit for the number of code fix iterations was exceeded");
                }
            }
            catch (Exception ex)
            {
                return (project, ExceptionDispatchInfo.Capture(ex));
            }

            return (project, null);
        }

        private static CodeAction? TryGetCodeActionToApply(List<CodeAction> actions, int? codeFixIndex, string? codeFixEquivalenceKey, IVerifier verifier)
        {
            if (codeFixIndex.HasValue && codeFixEquivalenceKey != null)
            {
                if (actions.Count <= codeFixIndex)
                {
                    return null;
                }

                verifier.Equal(
                    codeFixEquivalenceKey,
                    actions[codeFixIndex.Value].EquivalenceKey,
                    "The code action equivalence key and index must be consistent when both are specified.");

                return actions[codeFixIndex.Value];
            }
            else if (codeFixEquivalenceKey != null)
            {
                return actions.Find(x => x.EquivalenceKey == codeFixEquivalenceKey);
            }
            else if (actions.Count > (codeFixIndex ?? 0))
            {
                return actions[codeFixIndex ?? 0];
            }
            else
            {
                return null;
            }
        }

        /// <summary>
        /// Implements a workaround for issue #936, force re-parsing to get the same sort of syntax tree as the original document.
        /// </summary>
        /// <param name="project">The project to update.</param>
        /// <param name="verifier">The verifier to use for test assertions.</param>
        /// <param name="cancellationToken">The <see cref="CancellationToken"/>.</param>
        /// <returns>The updated <see cref="Project"/>.</returns>
        private async Task<Project> RecreateProjectDocumentsAsync(Project project, IVerifier verifier, CancellationToken cancellationToken)
        {
            foreach (var documentId in project.DocumentIds)
            {
                var document = project.GetDocument(documentId);
                var initialTree = await document.GetSyntaxTreeAsync(cancellationToken).ConfigureAwait(false);
                document = await RecreateDocumentAsync(document, cancellationToken).ConfigureAwait(false);
                var recreatedTree = await document.GetSyntaxTreeAsync(cancellationToken).ConfigureAwait(false);
                if (CodeFixValidationMode != CodeFixValidationMode.None)
                {
                    try
                    {
                        // We expect the tree produced by the code fix (initialTree) to match the form of the tree produced
                        // by the compiler for the same text (recreatedTree).
                        TreeEqualityVisitor.AssertNodesEqual(
                            verifier,
                            SyntaxKindType,
                            await recreatedTree.GetRootAsync(cancellationToken).ConfigureAwait(false),
                            await initialTree.GetRootAsync(cancellationToken).ConfigureAwait(false),
                            checkTrivia: CodeFixValidationMode == CodeFixValidationMode.Full);
                    }
                    catch
                    {
                        // Try to revalidate the tree with a better message
                        var renderedInitialTree = TreeToString(await initialTree.GetRootAsync(cancellationToken).ConfigureAwait(false), CodeFixValidationMode);
                        var renderedRecreatedTree = TreeToString(await recreatedTree.GetRootAsync(cancellationToken).ConfigureAwait(false), CodeFixValidationMode);
                        verifier.EqualOrDiff(renderedRecreatedTree, renderedInitialTree);

                        // This is not expected to be hit, but it will be hit if the validation failure occurred in a
                        // portion of the tree not captured by the rendered form from TreeToString.
                        throw;
                    }
                }

                project = document.Project;
            }

            return project;
        }

        private static async Task<Document> RecreateDocumentAsync(Document document, CancellationToken cancellationToken)
        {
            var newText = await document.GetTextAsync(cancellationToken).ConfigureAwait(false);
            return document.WithText(SourceText.From(newText.ToString(), newText.Encoding, newText.ChecksumAlgorithm));
        }

        /// <summary>
        /// Apply the inputted <see cref="CodeAction"/> to the inputted document.
        /// Meant to be used to apply code fixes.
        /// </summary>
        /// <param name="project">The <see cref="Project"/> to apply the fix on.</param>
        /// <param name="codeAction">A <see cref="CodeAction"/> that will be applied to the
        /// <paramref name="project"/>.</param>
        /// <param name="cancellationToken">The <see cref="CancellationToken"/> that the task will observe.</param>
        /// <returns>A <see cref="Project"/> with the changes from the <see cref="CodeAction"/>.</returns>
        private static async Task<Project> ApplyFixAsync(Project project, CodeAction codeAction, CancellationToken cancellationToken)
        {
            var operations = await codeAction.GetOperationsAsync(cancellationToken).ConfigureAwait(false);
            var solution = operations.OfType<ApplyChangesOperation>().Single().ChangedSolution;
            return solution.GetProject(project.Id);
        }

        private Task<(Project project, ExceptionDispatchInfo? iterationCountFailure)> FixAllAnalyzerDiagnosticsInDocumentAsync(ImmutableArray<DiagnosticAnalyzer> analyzers, ImmutableArray<CodeFixProvider> codeFixProviders, int? codeFixIndex, string? codeFixEquivalenceKey, Project project, int numberOfIterations, IVerifier verifier, CancellationToken cancellationToken)
        {
            return FixAllAnalyerDiagnosticsInScopeAsync(FixAllScope.Document, analyzers, codeFixProviders, codeFixIndex, codeFixEquivalenceKey, project, numberOfIterations, verifier, cancellationToken);
        }

        private Task<(Project project, ExceptionDispatchInfo? iterationCountFailure)> FixAllAnalyzerDiagnosticsInProjectAsync(ImmutableArray<DiagnosticAnalyzer> analyzers, ImmutableArray<CodeFixProvider> codeFixProviders, int? codeFixIndex, string? codeFixEquivalenceKey, Project project, int numberOfIterations, IVerifier verifier, CancellationToken cancellationToken)
        {
            return FixAllAnalyerDiagnosticsInScopeAsync(FixAllScope.Project, analyzers, codeFixProviders, codeFixIndex, codeFixEquivalenceKey, project, numberOfIterations, verifier, cancellationToken);
        }

        private Task<(Project project, ExceptionDispatchInfo? iterationCountFailure)> FixAllAnalyzerDiagnosticsInSolutionAsync(ImmutableArray<DiagnosticAnalyzer> analyzers, ImmutableArray<CodeFixProvider> codeFixProviders, int? codeFixIndex, string? codeFixEquivalenceKey, Project project, int numberOfIterations, IVerifier verifier, CancellationToken cancellationToken)
        {
            return FixAllAnalyerDiagnosticsInScopeAsync(FixAllScope.Solution, analyzers, codeFixProviders, codeFixIndex, codeFixEquivalenceKey, project, numberOfIterations, verifier, cancellationToken);
        }

        private async Task<(Project project, ExceptionDispatchInfo? iterationCountFailure)> FixAllAnalyerDiagnosticsInScopeAsync(FixAllScope scope, ImmutableArray<DiagnosticAnalyzer> analyzers, ImmutableArray<CodeFixProvider> codeFixProviders, int? codeFixIndex, string? codeFixEquivalenceKey, Project project, int numberOfIterations, IVerifier verifier, CancellationToken cancellationToken)
        {
            var expectedNumberOfIterations = numberOfIterations;
            if (numberOfIterations < 0)
            {
                numberOfIterations = -numberOfIterations;
            }

            var previousDiagnostics = ImmutableArray.Create<Diagnostic>();

            bool done;
            do
            {
                var analyzerDiagnostics = await GetSortedDiagnosticsAsync(project.Solution, analyzers, CompilerDiagnostics, cancellationToken).ConfigureAwait(false);
                if (analyzerDiagnostics.Length == 0)
                {
                    break;
                }

                if (!AreDiagnosticsDifferent(analyzerDiagnostics, previousDiagnostics))
                {
                    break;
                }

                try
                {
                    verifier.False(--numberOfIterations < -1, "The upper limit for the number of fix all iterations was exceeded");
                }
                catch (Exception ex)
                {
                    return (project, ExceptionDispatchInfo.Capture(ex));
                }

                Diagnostic? firstDiagnostic = null;
                CodeFixProvider? effectiveCodeFixProvider = null;
                string? equivalenceKey = null;
                foreach (var diagnostic in analyzerDiagnostics)
                {
                    var actions = new List<(CodeAction, CodeFixProvider)>();

                    foreach (var codeFixProvider in codeFixProviders)
                    {
                        if (!codeFixProvider.FixableDiagnosticIds.Contains(diagnostic.Id))
                        {
                            // do not pass unsupported diagnostics to a code fix provider
                            continue;
                        }

                        var context = new CodeFixContext(project.GetDocument(diagnostic.Location.SourceTree), diagnostic, (a, d) => actions.Add((a, codeFixProvider)), cancellationToken);
                        await codeFixProvider.RegisterCodeFixesAsync(context).ConfigureAwait(false);
                    }

                    var actionToApply = TryGetCodeActionToApply(actions.Select(a => a.Item1).ToList(), codeFixIndex, codeFixEquivalenceKey, verifier);
                    if (actionToApply != null)
                    {
                        firstDiagnostic = diagnostic;
                        effectiveCodeFixProvider = actions.SingleOrDefault(a => a.Item1 == actionToApply).Item2;
                        equivalenceKey = actionToApply.EquivalenceKey;
                        break;
                    }
                }

                var fixAllProvider = effectiveCodeFixProvider?.GetFixAllProvider();
                if (firstDiagnostic == null || fixAllProvider == null)
                {
                    numberOfIterations++;
                    break;
                }

                previousDiagnostics = analyzerDiagnostics;

                done = true;

                FixAllContext.DiagnosticProvider fixAllDiagnosticProvider = TestDiagnosticProvider.Create(analyzerDiagnostics);

                var analyzerDiagnosticIds = analyzers.SelectMany(x => x.SupportedDiagnostics).Select(x => x.Id);
                var compilerDiagnosticIds = codeFixProviders.SelectMany(codeFixProvider => codeFixProvider.FixableDiagnosticIds).Where(x => x.StartsWith("CS", StringComparison.Ordinal) || x.StartsWith("BC", StringComparison.Ordinal));
                var disabledDiagnosticIds = project.CompilationOptions.SpecificDiagnosticOptions.Where(x => x.Value == ReportDiagnostic.Suppress).Select(x => x.Key);
                var relevantIds = analyzerDiagnosticIds.Concat(compilerDiagnosticIds).Except(disabledDiagnosticIds).Distinct();
                var fixAllContext = new FixAllContext(project.GetDocument(firstDiagnostic.Location.SourceTree), effectiveCodeFixProvider, scope, equivalenceKey, relevantIds, fixAllDiagnosticProvider, cancellationToken);

                var action = await fixAllProvider.GetFixAsync(fixAllContext).ConfigureAwait(false);
                if (action == null)
                {
                    return (project, null);
                }

                var fixedProject = await ApplyFixAsync(project, action, cancellationToken).ConfigureAwait(false);
                if (fixedProject != project)
                {
                    done = false;

                    project = await RecreateProjectDocumentsAsync(fixedProject, verifier, cancellationToken).ConfigureAwait(false);
                }
            }
            while (!done);

            try
            {
                if (expectedNumberOfIterations >= 0)
                {
                    verifier.Equal(expectedNumberOfIterations, expectedNumberOfIterations - numberOfIterations, $"Expected '{expectedNumberOfIterations}' iterations but found '{expectedNumberOfIterations - numberOfIterations}' iterations.");
                }
                else
                {
                    verifier.True(numberOfIterations >= 0, "The upper limit for the number of code fix iterations was exceeded");
                }
            }
            catch (Exception ex)
            {
                return (project, ExceptionDispatchInfo.Capture(ex));
            }

            return (project, null);
        }

        /// <summary>
        /// Get the existing compiler diagnostics on the input document.
        /// </summary>
        /// <param name="project">The <see cref="Project"/> to run the compiler diagnostic analyzers on.</param>
        /// <param name="cancellationToken">The <see cref="CancellationToken"/> that the task will observe.</param>
        /// <returns>The compiler diagnostics that were found in the code.</returns>
        private static async Task<ImmutableArray<Diagnostic>> GetCompilerDiagnosticsAsync(Project project, CancellationToken cancellationToken)
        {
            var allDiagnostics = ImmutableArray.Create<Diagnostic>();

            foreach (var document in project.Documents)
            {
                var semanticModel = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
                allDiagnostics = allDiagnostics.AddRange(semanticModel.GetDiagnostics(cancellationToken: cancellationToken));
            }

            return allDiagnostics;
        }

        /// <summary>
        /// Given a document, turn it into a string based on the syntax root.
        /// </summary>
        /// <param name="document">The <see cref="Document"/> to be converted to a string.</param>
        /// <param name="cancellationToken">The <see cref="CancellationToken"/> that the task will observe.</param>
        /// <returns>A <see cref="SourceText"/> containing the syntax of the <see cref="Document"/> after formatting.</returns>
        private static async Task<SourceText> GetSourceTextFromDocumentAsync(Document document, CancellationToken cancellationToken)
        {
            var simplifiedDoc = await Simplifier.ReduceAsync(document, Simplifier.Annotation, cancellationToken: cancellationToken).ConfigureAwait(false);
            var formatted = await Formatter.FormatAsync(simplifiedDoc, Formatter.Annotation, cancellationToken: cancellationToken).ConfigureAwait(false);
            return await formatted.GetTextAsync(cancellationToken).ConfigureAwait(false);
        }

        private static bool AreDiagnosticsDifferent(ImmutableArray<Diagnostic> analyzerDiagnostics, ImmutableArray<Diagnostic> previousDiagnostics)
        {
            if (analyzerDiagnostics.Length != previousDiagnostics.Length)
            {
                return true;
            }

            for (var i = 0; i < analyzerDiagnostics.Length; i++)
            {
                if ((analyzerDiagnostics[i].Id != previousDiagnostics[i].Id)
                    || (analyzerDiagnostics[i].Location.SourceSpan != previousDiagnostics[i].Location.SourceSpan))
                {
                    return true;
                }
            }

            return false;
        }

        private string TreeToString(SyntaxNodeOrToken syntaxNodeOrToken, CodeFixValidationMode validationMode)
        {
            var result = new StringBuilder();
            TreeToString(syntaxNodeOrToken, string.Empty, validationMode, result);
            return result.ToString();
        }

        private void TreeToString(SyntaxNodeOrToken syntaxNodeOrToken, string indent, CodeFixValidationMode validationMode, StringBuilder result)
        {
            if (syntaxNodeOrToken.IsNode)
            {
                result.AppendLine($"{indent}Node({Kind(syntaxNodeOrToken.RawKind)}):");

                var childIndent = indent + "  ";
                foreach (var child in syntaxNodeOrToken.ChildNodesAndTokens())
                {
                    TreeToString(child, childIndent, validationMode, result);
                }
            }
            else
            {
                var syntaxToken = syntaxNodeOrToken.AsToken();
                result.AppendLine($"{indent}Token({Kind(syntaxToken.RawKind)}): {Escape(syntaxToken.Text)}");

                if (validationMode == CodeFixValidationMode.Full)
                {
                    var childIndent = indent + "  ";
                    foreach (var trivia in syntaxToken.LeadingTrivia)
                    {
                        if (trivia.HasStructure)
                        {
                            result.AppendLine($"{childIndent}Leading({Kind(trivia.RawKind)}):");
                            TreeToString(trivia.GetStructure(), childIndent + "  ", validationMode, result);
                        }
                        else
                        {
                            result.AppendLine($"{childIndent}Leading({Kind(trivia.RawKind)}): {Escape(trivia.ToString())}");
                        }
                    }

                    foreach (var trivia in syntaxToken.TrailingTrivia)
                    {
                        if (trivia.HasStructure)
                        {
                            result.AppendLine($"{childIndent}Trailing({Kind(trivia.RawKind)}):");
                            TreeToString(trivia.GetStructure(), childIndent + "  ", validationMode, result);
                        }
                        else
                        {
                            result.AppendLine($"{childIndent}Trailing({Kind(trivia.RawKind)}): {Escape(trivia.ToString())}");
                        }
                    }
                }
            }

            // Local functions
            string Escape(string text)
            {
                return text
                    .Replace("\\", "\\\\")
                    .Replace("\t", "\\t")
                    .Replace("\r", "\\r")
                    .Replace("\n", "\\n");
            }

            string Kind(int syntaxKind)
            {
                if (SyntaxKindType.GetTypeInfo()?.IsEnum ?? false)
                {
                    return Enum.Format(SyntaxKindType, (ushort)syntaxKind, "G");
                }
                else
                {
                    return syntaxKind.ToString();
                }
            }
        }

        private sealed class SourceFileEqualityComparer : IEqualityComparer<(string filename, SourceText content)>
        {
            private SourceFileEqualityComparer()
            {
            }

            public static SourceFileEqualityComparer Instance { get; } = new SourceFileEqualityComparer();

            public bool Equals((string filename, SourceText content) x, (string filename, SourceText content) y)
            {
                if (x.filename != y.filename)
                {
                    return false;
                }

                if (x.content is null || y.content is null)
                {
                    return ReferenceEquals(x, y);
                }

                return x.content.Encoding == y.content.Encoding
                    && x.content.ChecksumAlgorithm == y.content.ChecksumAlgorithm
                    && x.content.ContentEquals(y.content);
            }

            public int GetHashCode((string filename, SourceText content) obj)
            {
                return obj.filename.GetHashCode()
                    ^ (obj.content?.ToString().GetHashCode() ?? 0);
            }
        }

        private class TreeEqualityVisitor
        {
            private readonly IVerifier _verifier;
            private readonly Type _syntaxKindType;
            private readonly SyntaxNode _expected;
            private readonly bool _checkTrivia;

            private TreeEqualityVisitor(IVerifier verifier, Type syntaxKindType, SyntaxNode expected, bool checkTrivia)
            {
                _verifier = verifier;
                _syntaxKindType = syntaxKindType;
                _expected = expected ?? throw new ArgumentNullException(nameof(expected));
                _checkTrivia = checkTrivia;
            }

            public void Visit(SyntaxNode node)
            {
                AssertSyntaxKindEqual(_expected.RawKind, node.RawKind);
                AssertChildSyntaxListEqual(_expected.ChildNodesAndTokens(), node.ChildNodesAndTokens(), _checkTrivia);
            }

            internal static void AssertNodesEqual(IVerifier verifier, Type syntaxKindType, SyntaxNode expected, SyntaxNode actual, bool checkTrivia)
            {
                new TreeEqualityVisitor(verifier, syntaxKindType, expected, checkTrivia).Visit(actual);
            }

            private void AssertNodesEqual(SyntaxNode expected, SyntaxNode actual, bool checkTrivia)
            {
                AssertNodesEqual(_verifier, _syntaxKindType, expected, actual, checkTrivia);
            }

            private void AssertChildSyntaxListEqual(ChildSyntaxList expected, ChildSyntaxList actual, bool checkTrivia)
            {
                _verifier.Equal(expected.Count, actual.Count);
                foreach (var (expectedChild, actualChild) in expected.Zip(actual, (first, second) => (first, second)))
                {
                    if (expectedChild.IsToken)
                    {
                        _verifier.True(actualChild.IsToken);
                        AssertTokensEqual(expectedChild.AsToken(), actualChild.AsToken(), checkTrivia);
                    }
                    else
                    {
                        _verifier.True(actualChild.IsNode);
                        AssertNodesEqual(expectedChild.AsNode(), actualChild.AsNode(), checkTrivia);
                    }
                }
            }

            private void AssertTokensEqual(SyntaxToken expected, SyntaxToken actual, bool checkTrivia)
            {
                AssertTriviaListEqual(expected.LeadingTrivia, actual.LeadingTrivia, checkTrivia);
                AssertSyntaxKindEqual(expected.RawKind, actual.RawKind);
                _verifier.Equal(expected.Value, actual.Value);
                _verifier.Equal(expected.Text, actual.Text);
                _verifier.Equal(expected.ValueText, actual.ValueText);
                AssertTriviaListEqual(expected.TrailingTrivia, actual.TrailingTrivia, checkTrivia);
            }

            private void AssertTriviaListEqual(SyntaxTriviaList expected, SyntaxTriviaList actual, bool checkTrivia)
            {
                if (!checkTrivia)
                {
                    return;
                }

                for (var i = 0; i < Math.Min(expected.Count, actual.Count); i++)
                {
                    AssertTriviaEqual(expected[i], actual[i], checkTrivia);
                }

                _verifier.Equal(expected.Count, actual.Count);
            }

            private void AssertTriviaEqual(SyntaxTrivia expected, SyntaxTrivia actual, bool checkTrivia)
            {
                if (!checkTrivia)
                {
                    return;
                }

                AssertSyntaxKindEqual(expected.RawKind, actual.RawKind);
                _verifier.Equal(expected.HasStructure, actual.HasStructure);
                _verifier.Equal(expected.IsDirective, actual.IsDirective);
                _verifier.Equal(expected.GetAnnotations(), actual.GetAnnotations());
                if (expected.HasStructure)
                {
                    AssertNodesEqual(expected.GetStructure(), actual.GetStructure(), checkTrivia);
                }
            }

            private void AssertSyntaxKindEqual(int expected, int actual)
            {
                if (expected == actual)
                {
                    return;
                }

                if (_syntaxKindType.GetTypeInfo()?.IsEnum ?? false)
                {
                    _verifier.Equal(
                        Enum.Format(_syntaxKindType, (ushort)expected, "G"),
                        Enum.Format(_syntaxKindType, (ushort)actual, "G"));
                }
                else
                {
                    _verifier.Equal(expected, actual);
                }
            }
        }
    }
}
