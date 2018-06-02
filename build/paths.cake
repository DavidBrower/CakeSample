public static class Paths
{
    public static FilePath SolutionFile => "RHINDSystem.sln";
    public static FilePath OpenCoverResultFile => "opencover-coverage.xml";
    public static FilePath CoberturaResultFile => "cobertura-coverage.xml";
    public static FilePath OpenCoverToCobertura => "./tools/OpenCoverToCoberturaConverter.0.3.2/tools/OpenCoverToCoberturaConverter.exe";
    public static DirectoryPath CodeCoverageReportDirectory => "coverage";
    public static FilePath WebNuspecFile => "web/Web.nuspec";
}
