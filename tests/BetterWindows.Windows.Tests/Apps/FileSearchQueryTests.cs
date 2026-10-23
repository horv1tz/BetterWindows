using System.Text;

namespace BetterWindows.Native.Tests.Apps;

public class FileSearchQueryTests
{
    /// <summary>Replaces every complete single-quoted SQL literal by '?' and fails if a literal is left open: what remains is the statement skeleton.</summary>
    private static string Skeleton(string sql)
    {
        var sb = new StringBuilder();
        bool inLiteral = false;
        for (int i = 0; i < sql.Length; i++)
        {
            char c = sql[i];
            if (c == '\'')
            {
                if (inLiteral && i + 1 < sql.Length && sql[i + 1] == '\'') { i++; continue; } // doubled quote = escaped quote
                inLiteral = !inLiteral;
                if (!inLiteral) sb.Append("'?'");
                continue;
            }
            if (!inLiteral) sb.Append(c);
        }
        inLiteral.Should().BeFalse("every string literal must be closed: user input must not leak out of it. SQL: " + sql);
        return sb.ToString();
    }

    [Theory]
    [InlineData("O'Brien", "O''Brien")]
    [InlineData("'", "''")]
    [InlineData("a''b", "a''''b")]
    [InlineData("50%", "50")]
    [InlineData("100% [done]", "100 done")]
    [InlineData("a^b", "ab")]
    [InlineData("tab\there\u0000nul\u0007bell", "tabherenulbell")]
    [InlineData("under_score", "under_score")]
    [InlineData("Привет мир", "Привет мир")]
    [InlineData("C:\\dir\\file", "C:\\dir\\file")]
    public void Literal_escaping(string input, string expected) => FileSearchQuery.EscapeLiteral(input).Should().Be(expected);

    [Fact]
    public void Plan_is_null_for_empty_or_wildcard_only_input()
    {
        FileSearchQuery.Plan(null, 10).Should().BeNull();
        FileSearchQuery.Plan("", 10).Should().BeNull();
        FileSearchQuery.Plan("   ", 10).Should().BeNull();
        FileSearchQuery.Plan("%%[]^", 10).Should().BeNull();
    }

    [Fact]
    public void Plan_has_a_prefix_phase_then_a_contains_phase_over_the_index()
    {
        var plan = FileSearchQuery.Plan("report", 20)!;
        plan.Limit.Should().Be(20);
        plan.Statements.Should().HaveCount(2);
        plan.Statements[0].Should().Contain("FROM SYSTEMINDEX").And.Contain("SCOPE='file:'").And.Contain("System.FileName LIKE 'report%'");
        plan.Statements[1].Should().Contain("System.FileName LIKE '%report%'");
        plan.Statements.Should().OnlyContain(s => s.Contains("ORDER BY System.Search.Rank DESC"));
        plan.Statements.Should().OnlyContain(s => s.StartsWith("SELECT TOP "));
        plan.Statements.Should().OnlyContain(s => s.Contains("System.ItemPathDisplay") && s.Contains("System.ItemNameDisplay") && s.Contains("System.DateModified"));
    }

    [Fact]
    public void Multiple_words_must_all_appear_in_the_contains_phase()
    {
        var plan = FileSearchQuery.Plan("annual  report 2024", 10)!;
        plan.Statements[0].Should().Contain("LIKE 'annual report 2024%'");
        plan.Statements[1].Should().Contain("LIKE '%annual%'").And.Contain("LIKE '%report%'").And.Contain("LIKE '%2024%'");
        plan.Statements[1].Split(" AND ").Length.Should().Be(4, "the scope condition and three token conditions are AND-ed");
    }

    [Theory]
    [InlineData("x'; DROP TABLE SYSTEMINDEX; --")]
    [InlineData("x' OR '1'='1")]
    [InlineData("x'' OR ''1''=''1")]
    [InlineData("'")]
    [InlineData("''''''")]
    [InlineData("a' UNION SELECT System.ItemPathDisplay FROM SYSTEMINDEX WHERE '1'='1")]
    [InlineData("x\\' OR 1=1 --")]
    [InlineData("x%' OR System.FileName LIKE '%")]
    public void Hostile_input_never_changes_the_statement_structure(string hostile)
    {
        var benign = FileSearchQuery.Plan("benign", 10)!;
        var evil = FileSearchQuery.Plan(hostile, 10)!;
        // The skeleton (SQL with the literals blanked out) of the first statement has the same shape except for the number of AND-ed contains terms in phase 2.
        Skeleton(evil.Statements[0]).Should().Be(Skeleton(benign.Statements[0]));
        // Phase 2: only LIKE '?' terms joined by AND, nothing else
        var s2 = Skeleton(evil.Statements[1]);
        s2.Should().StartWith("SELECT TOP ").And.Contain("WHERE SCOPE='?' AND System.FileName LIKE '?'");
        s2.Replace("System.FileName LIKE '?'", "T").Should().MatchRegex(@"WHERE SCOPE='\?' AND T( AND T)* ORDER BY System\.Search\.Rank DESC, System\.DateModified DESC$");
    }

    [Fact]
    public void There_are_simpler_fallback_variants_with_the_same_phases()
    {
        var plan = FileSearchQuery.Plan("report", 10)!;
        plan.Variants.Should().HaveCount(3);
        plan.Variants.Should().OnlyContain(v => v.Count == 2);
        plan.Variants[0].Should().OnlyContain(s => s.Contains("System.Search.Rank") && s.Contains("SCOPE='file:'"));
        plan.Variants[1].Should().OnlyContain(s => !s.Contains("System.Search.Rank") && s.Contains("SCOPE='file:'") && s.Contains("ORDER BY System.DateModified DESC"));
        plan.Variants[2].Should().OnlyContain(s => !s.Contains("System.Search.Rank") && !s.Contains("SCOPE"));
        // the match conditions are identical in every variant
        foreach (var phase in new[] { 0, 1 })
        {
            var conditions = plan.Variants.Select(v => System.Text.RegularExpressions.Regex.Match(v[phase], @"(System\.FileName LIKE .*?)( ORDER BY)").Groups[1].Value).Distinct().ToList();
            conditions.Should().HaveCount(1);
        }
    }

    [Theory]
    [InlineData("x'; DROP TABLE SYSTEMINDEX; --")]
    [InlineData("a' UNION SELECT System.ItemPathDisplay FROM SYSTEMINDEX WHERE '1'='1")]
    public void Every_fallback_variant_is_as_injection_proof_as_the_first(string hostile)
    {
        var plan = FileSearchQuery.Plan(hostile, 10)!;
        foreach (var statement in plan.Variants.SelectMany(v => v)) Skeleton(statement); // asserts balanced literals
    }

    [Fact]
    public void Limits_are_clamped_and_over_fetch_is_bounded()
    {
        FileSearchQuery.Plan("a", 0)!.Limit.Should().Be(1);
        FileSearchQuery.Plan("a", 100000)!.Limit.Should().Be(FileSearchQuery.MaxLimit);
        FileSearchQuery.Plan("a", -5)!.Limit.Should().Be(1);
        var top = int.Parse(System.Text.RegularExpressions.Regex.Match(FileSearchQuery.Plan("a", 100)!.Statements[0], @"TOP (\d+)").Groups[1].Value);
        top.Should().BeLessThanOrEqualTo(FileSearchQuery.MaxLimit * 3);
    }

    [Fact]
    public void Token_count_and_length_are_capped()
    {
        FileSearchQuery.Tokenize(string.Join(' ', Enumerable.Range(0, 50).Select(i => "w" + i))).Should().HaveCount(FileSearchQuery.MaxTokens);
        string.Concat(FileSearchQuery.Tokenize(new string('a', 5000))).Length.Should().BeLessThanOrEqualTo(FileSearchQuery.MaxQueryLength);
    }

    [Theory]
    [InlineData(@"C:\Users\me\Documents\report.docx", false)]
    [InlineData(@"D:\Work\notes.txt", false)]
    [InlineData(@"C:\Users\me\AppData\Local\Temp\x.tmp", true)]
    [InlineData(@"C:\Windows\System32\notepad.exe", true)]
    [InlineData(@"C:\Program Files\Foo\foo.exe", true)]
    [InlineData(@"C:\Program Files (x86)\Foo\foo.exe", true)]
    [InlineData(@"C:\ProgramData\Microsoft\Windows\Start Menu\Programs\x.lnk", true)]
    [InlineData(@"C:\$Recycle.Bin\S-1-5\x", true)]
    [InlineData(@"C:\Users\me\code\proj\node_modules\x\index.js", true)]
    [InlineData(@"C:\Users\me\code\proj\.git\config", true)]
    [InlineData("", true)]
    public void System_and_tooling_folders_are_excluded(string path, bool excluded) => FileSearchQuery.IsExcludedPath(path).Should().Be(excluded);

    [Fact]
    public void A_user_folder_that_merely_resembles_a_system_one_is_kept()
    {
        FileSearchQuery.IsExcludedPath(@"C:\Users\me\My Windows Notes\a.txt").Should().BeFalse();
        FileSearchQuery.IsExcludedPath(@"C:\Users\me\appdata-backup\a.txt").Should().BeFalse();
    }
}
