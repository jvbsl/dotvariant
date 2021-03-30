//
// Copyright Miro Knejp 2021.
// Distributed under the Boost Software License, Version 1.0.
// (See accompanying file LICENSE.txt or copy at https://www.boost.org/LICENSE_1_0.txt)
//

using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.ComponentModel.DataAnnotations.Schema;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using static dotVariant.Generator.Test.GeneratorTools;

namespace dotVariant.Generator.Test
{
    [TestOf(typeof(SourceGenerator))]
    [Parallelizable(ParallelScope.All)]
    internal static class VariantSourceGenerator_Test
    {
        private static Dictionary<string, string>? _extraSources;

        [OneTimeSetUp]
        public static void LoadAttribute()
        {
            _extraSources = new()
            {
                { "VariantAttribute.cs", LoadSample("VariantAttribute.cs") },
                { "TypeMismatchException.cs", LoadSample("TypeMismatchException.cs") },
            };
        }

        [TestCaseSource(nameof(TranslationCases))]
        public static void Translation(string typeName, string input, string expected)
        {
            var sources = new Dictionary<string, string>(_extraSources!)
            {
                ["input"] = input
            };
            var outputs = GetGeneratedOutput<SourceGenerator>(sources);
            var file = $"{typeof(SourceGenerator).Assembly.GetName().Name}\\{typeof(SourceGenerator).FullName}\\{typeName}.cs";
            var output = outputs[file];
            if (output != StripCopyrightHeader(expected))
            {
                // TODO: create diff
                Assert.That(output, Is.EqualTo(expected));
            }
        }

        public static IEnumerable<TestCaseData> TranslationCases()
            => new (string FileName, string TypeName)[]
                {
                    ("Variant-class", "Foo.Variant_class"),
                    ("Variant-struct", "Foo.Variant_struct"),
                }
                .Select(
                    test => new TestCaseData(
                        test.TypeName,
                        LoadSample($"{test.FileName}.in.cs"),
                        LoadSample($"{test.FileName}.out.cs"))
                    .SetName($"{nameof(Translation)}({test.FileName})"));

        private static string StripCopyrightHeader(string expected)
        {
            // The test file saved on disk contains a copyright header that is not
            // produced by the generator. Remove it from the expected output,
            // i.e. everything before the first non-empty non-comment line.

            using var reader = new StringReader(expected);
            var line = reader.ReadLine();
            for (; line is not null; line = reader.ReadLine())
            {
                if (line.Length > 0 && line[0] != '/')
                {
                    return line + Environment.NewLine + reader.ReadToEnd();
                }
            }
            return expected;
        }

        [TestCaseSource(nameof(DiagnosticsCases))]
        public static void Diagnostics(string input)
        {
            var sources = new Dictionary<string, string>(_extraSources!)
            {
                ["input"] = input
            };
            var expectations = ExtractExpectations(input);
            var diags =
                GetGeneratorDiagnostics<SourceGenerator>(sources)
                .Select(diag =>
                {
                    var position = diag.Location.GetMappedLineSpan().StartLinePosition;
                    return (Line: position.Line + 1, Column: position.Character + 1, diag.Id);
                });

            var unfulfilledExpectations =
                expectations
                .Where(exp => !diags.Any(diag => CompareDiagnostics(diag, exp)))
                .ToImmutableArray();
            var unexpectedDiagnostics =
                diags
                .Where(diag => !expectations.Any(exp => CompareDiagnostics(diag, exp)))
                .ToImmutableArray();

            Assert.Multiple(() =>
            {
                if (unfulfilledExpectations.Any())
                {
                    Assert.Fail(
                        string.Join(
                            Environment.NewLine,
                            unfulfilledExpectations.Select(ex => $"    {ex.Line}:{ex.Column}: {ex.Id}")
                            .Prepend("Unfilfilled diagnostic expectations:")));
                }
                if (unexpectedDiagnostics.Any())
                {
                    Assert.Fail(
                        string.Join(
                            Environment.NewLine,
                            unexpectedDiagnostics.Select(ex => $"    {ex.Line}:{ex.Column}: {ex.Id}")
                            .Prepend("Unexpected diagnostics:")));
                }
            });
        }

        private static bool CompareDiagnostics((int Line, int Column, string Id) lhs, (int Line, int Column, string Id) rhs)
        {
            if (lhs.Line != rhs.Line || lhs.Id != rhs.Id)
            {
                return false;
            }
            else if (lhs.Column == rhs.Column)
            {
                return true;
            }
            else if (lhs.Column == -1 || rhs.Column == -1)
            {
                return true;
            }
            else
            {
                return false;
            }
        }

        private static IEnumerable<TestCaseData> DiagnosticsCases()
        {
            var assembly = Assembly.GetExecutingAssembly()!;
            var prefix = $"{assembly.GetName().Name!}.diagnostics.";
            var testPattern = new Regex($@"^(.*)\.cs$");

            return
                assembly
                .GetManifestResourceNames()
                .Where(name => name.StartsWith(prefix))
                .Select(name =>
                {
                    var match = testPattern.Match(name[prefix.Length..]);
                    return (Name: match.Groups[1].Value, Input: Load());

                    string Load()
                    {
                        using var stream = assembly.GetManifestResourceStream(name);
                        return new StreamReader(stream!).ReadToEnd();
                    }
                })
                .Select(test =>
                    new TestCaseData(test.Input)
                    .SetName($"{nameof(Diagnostics)}({test.Name})"));
        }
    }
}
