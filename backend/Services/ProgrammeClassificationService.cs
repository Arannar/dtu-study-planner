using System.Text.RegularExpressions;
using Planner.Backend.Models;
using Planner.Backend.Soap.Courseblocks;
namespace Planner.Backend.Services;

public interface IProgrammeClassificationService
{
    Task<ProgrammeStudyBoxClassification> ResolveAsync(ProgrammeListItem programme, int requestedVolume);
}

public sealed class ProgrammeClassificationService(IDtuGateway gateway, ILogger<ProgrammeClassificationService> logger) : IProgrammeClassificationService
{
    public async Task<ProgrammeStudyBoxClassification> ResolveAsync(
        ProgrammeListItem programme,
        int requestedVolume)
    {
        foreach (var candidateVolume in BuildStudyBoxVolumeCandidates(requestedVolume))
        {
            var studyBoxes = await gateway.GetStudyBoxesAsync(candidateVolume);
            var classification = ParseProgrammeClassificationFromStudyBoxes(programme, studyBoxes ?? []);
            if (classification.HasData)
            {
                logger.LogInformation(
                    "Resolved StudyBox classification programme={ProgrammeCode}, level={Level}, volume={Volume}, mandatoryCount={MandatoryCount}, approvedMscCount={ApprovedMscCount}",
                    programme.Code,
                    programme.Level,
                    candidateVolume,
                    classification.MandatoryCourses.Count,
                    classification.ApprovedMscElectiveCourseCodes.Count);

                return classification with { SourceVolume = candidateVolume };
            }
        }

        return new ProgrammeStudyBoxClassification();
    }

    private static IEnumerable<int> BuildStudyBoxVolumeCandidates(int requestedVolume)
    {
        if (requestedVolume > 0)
        {
            yield return requestedVolume;
        }

        if (requestedVolume > 1)
        {
            yield return requestedVolume - 1;
        }
    }

    private static ProgrammeStudyBoxClassification ParseProgrammeClassificationFromStudyBoxes(
        ProgrammeListItem programme,
        IEnumerable<StudyBoxInfo> studyBoxes)
    {
        return programme.Level.ToLowerInvariant() switch
        {
            "bsc" => ParseBscProgrammeClassificationFromStudyBoxes(programme, studyBoxes),
            "beng" => ParseBengProgrammeClassificationFromStudyBoxes(programme, studyBoxes),
            "msc" => ParseMscProgrammeClassificationFromStudyBoxes(programme, studyBoxes),
            _ => new ProgrammeStudyBoxClassification()
        };
    }

    private static ProgrammeStudyBoxClassification ParseBscProgrammeClassificationFromStudyBoxes(
        ProgrammeListItem programme,
        IEnumerable<StudyBoxInfo> studyBoxes)
    {
        var aliases = BuildProgrammeAliases(programme);
        var mandatory = new List<ParsedProgrammeCourse>();
        var approvedMsc = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var box in studyBoxes ?? [])
        {
            var courseCodes = NormalizeStudyBoxCourseCodes(box.CourseCodes);
            if (courseCodes.Count == 0)
            {
                continue;
            }

            if (TryParseBscMandatoryStudyBox(aliases, box, out var parsedMandatory))
            {
                mandatory.AddRange(courseCodes.Select(courseCode => new ParsedProgrammeCourse
                {
                    CourseCode = courseCode,
                    Title = courseCode,
                    Bucket = parsedMandatory.Bucket,
                    BucketLabel = parsedMandatory.Label,
                    SourceOrder = mandatory.Count + 1
                }));
                continue;
            }

            if (TryParseBscApprovedMscStudyBox(aliases, box))
            {
                foreach (var courseCode in courseCodes)
                {
                    approvedMsc.Add(courseCode);
                }
            }
        }

        return new ProgrammeStudyBoxClassification
        {
            MandatoryCourses = mandatory
                .GroupBy(item => item.CourseCode, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .OrderBy(item => item.SourceOrder)
                .ToList(),
            ApprovedMscElectiveCourseCodes = approvedMsc
                .OrderBy(code => code, StringComparer.OrdinalIgnoreCase)
                .ToList()
        };
    }

    private static ProgrammeStudyBoxClassification ParseBengProgrammeClassificationFromStudyBoxes(
        ProgrammeListItem programme,
        IEnumerable<StudyBoxInfo> studyBoxes)
    {
        var aliases = BuildProgrammeAliases(programme);
        var mandatory = new List<ParsedProgrammeCourse>();

        foreach (var box in studyBoxes ?? [])
        {
            var courseCodes = NormalizeStudyBoxCourseCodes(box.CourseCodes);
            if (courseCodes.Count == 0)
            {
                continue;
            }

            if (!TryParseBengMandatoryStudyBox(aliases, box, out var label))
            {
                continue;
            }

            mandatory.AddRange(courseCodes.Select(courseCode => new ParsedProgrammeCourse
            {
                CourseCode = courseCode,
                Title = courseCode,
                Bucket = "mandatory",
                BucketLabel = label,
                SourceOrder = mandatory.Count + 1
            }));
        }

        return new ProgrammeStudyBoxClassification
        {
            MandatoryCourses = mandatory
                .GroupBy(item => item.CourseCode, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .OrderBy(item => item.SourceOrder)
                .ToList()
        };
    }

    private static ProgrammeStudyBoxClassification ParseMscProgrammeClassificationFromStudyBoxes(
        ProgrammeListItem programme,
        IEnumerable<StudyBoxInfo> studyBoxes)
    {
        var aliases = BuildProgrammeAliases(programme);
        var mandatory = new List<ParsedProgrammeCourse>();

        foreach (var box in studyBoxes ?? [])
        {
            var courseCodes = NormalizeStudyBoxCourseCodes(box.CourseCodes);
            if (courseCodes.Count == 0)
            {
                continue;
            }

            if (!TryParseMscMandatoryStudyBox(aliases, box, out var parsedMandatory))
            {
                continue;
            }

            mandatory.AddRange(courseCodes.Select(courseCode => new ParsedProgrammeCourse
            {
                CourseCode = courseCode,
                Title = courseCode,
                Bucket = parsedMandatory.Bucket,
                BucketLabel = parsedMandatory.Label,
                SourceOrder = mandatory.Count + 1
            }));
        }

        return new ProgrammeStudyBoxClassification
        {
            MandatoryCourses = mandatory
                .GroupBy(item => item.CourseCode, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .OrderBy(item => item.SourceOrder)
                .ToList()
        };
    }

    private static HashSet<string> BuildProgrammeAliases(ProgrammeListItem programme)
    {
        var aliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        AddAlias(aliases, programme.ProgrammeNameDanish);
        AddAlias(aliases, programme.ProgrammeNameEnglish);
        AddAlias(aliases, programme.PopularTitleDanish);
        AddAlias(aliases, programme.PopularTitleEnglish);

        return aliases;

        static void AddAlias(HashSet<string> target, string? value)
        {
            var normalized = NormalizeProgrammeName(value);
            if (!string.IsNullOrWhiteSpace(normalized))
            {
                target.Add(normalized);
            }
        }
    }

    private static List<string> NormalizeStudyBoxCourseCodes(IEnumerable<string>? courseCodes)
    {
        return (courseCodes ?? [])
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .Select(code => code.Trim())
            .Where(code => Regex.IsMatch(code, @"^\d{5}$"))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(code => code, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static bool TryParseBscMandatoryStudyBox(
        IReadOnlyCollection<string> aliases,
        StudyBoxInfo box,
        out (string Bucket, string Label) parsed)
    {
        parsed = default;

        foreach (var label in EnumerateStudyBoxLabels(box))
        {
            if (TryParseStrictProgrammeStudyBoxLabel(
                    label,
                    "Polyteknisk grundlag (BSc), ",
                    aliases,
                    out _))
            {
                parsed = ("polytechnicalFoundation", label.Trim());
                return true;
            }

            if (TryParseStrictProgrammeStudyBoxLabel(
                    label,
                    "Polytechnical foundation (BSc), ",
                    aliases,
                    out _))
            {
                parsed = ("polytechnicalFoundation", label.Trim());
                return true;
            }

            if (TryParseStrictProgrammeStudyBoxLabel(
                    label,
                    "Retningsspecifikt kursus (BSc), ",
                    aliases,
                    out _))
            {
                parsed = ("programmeSpecific", label.Trim());
                return true;
            }

            if (TryParseStrictProgrammeStudyBoxLabel(
                    label,
                    "Programme specific course (BSc), ",
                    aliases,
                    out _))
            {
                parsed = ("programmeSpecific", label.Trim());
                return true;
            }

            if (TryParseStrictProgrammeStudyBoxLabel(
                    label,
                    "Projekter (BSc), ",
                    aliases,
                    out _))
            {
                parsed = ("projects", label.Trim());
                return true;
            }

            if (TryParseStrictProgrammeStudyBoxLabel(
                    label,
                    "Projects (BSc), ",
                    aliases,
                    out _))
            {
                parsed = ("projects", label.Trim());
                return true;
            }
        }

        return false;
    }

    private static bool TryParseBscApprovedMscStudyBox(
        IReadOnlyCollection<string> aliases,
        StudyBoxInfo box)
    {
        return EnumerateStudyBoxLabels(box).Any(label =>
            TryParseStrictProgrammeStudyBoxLabel(label, "Valgfrit kandidatkursus (BSc), ", aliases, out _) ||
            TryParseStrictProgrammeStudyBoxLabel(label, "Elective MSc course (BSc), ", aliases, out _));
    }

    private static bool TryParseBengMandatoryStudyBox(
        IReadOnlyCollection<string> aliases,
        StudyBoxInfo box,
        out string label)
    {
        label = string.Empty;

        foreach (var candidateLabel in EnumerateStudyBoxLabels(box))
        {
            if (TryParseStrictProgrammeStudyBoxLabel(candidateLabel, "Obligatorisk kursus (B Eng), ", aliases, out _) ||
                TryParseStrictProgrammeStudyBoxLabel(candidateLabel, "Mandatory course (B Eng), ", aliases, out _) ||
                TryParseStrictProgrammeStudyBoxLabel(candidateLabel, "Mandatory course, Bachelor of Engineering ", aliases, out _))
            {
                label = candidateLabel.Trim();
                return true;
            }
        }

        return false;
    }

    private static bool TryParseMscMandatoryStudyBox(
        IReadOnlyCollection<string> aliases,
        StudyBoxInfo box,
        out (string Bucket, string Label) parsed)
    {
        parsed = default;

        foreach (var label in EnumerateStudyBoxLabels(box))
        {
            if (TryParseStrictProgrammeStudyBoxLabel(label, "Polyteknisk grundlag (MSc), ", aliases, out _) ||
                TryParseStrictProgrammeStudyBoxLabel(label, "Polytechnical foundation (MSc), ", aliases, out _))
            {
                parsed = ("polytechnicalFoundation", label.Trim());
                return true;
            }

            if (TryParseStrictProgrammeStudyBoxLabel(label, "Retningsspecifikt kursus (MSc), ", aliases, out _) ||
                TryParseStrictProgrammeStudyBoxLabel(label, "Programme specific course (MSc), ", aliases, out _))
            {
                parsed = ("programmeSpecific", label.Trim());
                return true;
            }

            if (TryParseStrictProgrammeStudyBoxLabel(label, "Projekter (MSc), ", aliases, out _) ||
                TryParseStrictProgrammeStudyBoxLabel(label, "Projects (MSc), ", aliases, out _))
            {
                parsed = ("projects", label.Trim());
                return true;
            }
        }

        return false;
    }

    private static bool TryParseStrictProgrammeStudyBoxLabel(
        string? label,
        string requiredPrefix,
        IReadOnlyCollection<string> aliases,
        out string programmeName)
    {
        programmeName = string.Empty;

        if (string.IsNullOrWhiteSpace(label) || aliases.Count == 0)
        {
            return false;
        }

        var trimmedLabel = label.Trim();
        if (!trimmedLabel.StartsWith(requiredPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        programmeName = NormalizeProgrammeName(trimmedLabel[requiredPrefix.Length..]);
        return aliases.Contains(programmeName);
    }

    private static IEnumerable<string> EnumerateStudyBoxLabels(StudyBoxInfo box)
    {
        if (!string.IsNullOrWhiteSpace(box.Danish))
        {
            yield return box.Danish;
        }

        if (!string.IsNullOrWhiteSpace(box.English))
        {
            yield return box.English;
        }
    }

    public static string NormalizeProgrammeName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = value
            .Replace("&", " og ", StringComparison.Ordinal)
            .Replace(" and ", " og ", StringComparison.OrdinalIgnoreCase)
            .Replace("-", " ", StringComparison.Ordinal)
            .Replace(".", " ", StringComparison.Ordinal)
            .Replace(",", " ", StringComparison.Ordinal);

        normalized = Regex.Replace(normalized, @"\s+", " ").Trim();
        return normalized.ToLowerInvariant();
    }

}
