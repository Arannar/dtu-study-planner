using System.Globalization;

namespace Planner.Backend.Services;

public readonly record struct AcademicYear
{
    public int StartYear { get; }
    public string Catalogue => $"{StartYear.ToString(CultureInfo.InvariantCulture)}/{(StartYear + 1).ToString(CultureInfo.InvariantCulture)}";

    public AcademicYear(int startYear)
    {
        if (startYear is < 1900 or > 9998)
            throw new ArgumentException("Volume must be an academic starting year between 1900 and 9998.");
        StartYear = startYear;
    }
}
