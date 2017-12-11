#load build/paths.cake
#load build/urls.cake
#tool nuget:?package=xunit.runner.console&version=2.2.0
#tool nuget:?package=OpenCover&version=4.6.519
#tool nuget:?package=ReportGenerator&version=2.5.8
#tool nuget:?package=GitVersion.CommandLine
#tool nuget:?package=OctopusTools&version=4.27.3

var projectName = ""; //provide the name of the project
var target = Argument("Target", "Build");
var configuration = Argument("Configuration", "Release");
var codeCoverageReportPath = Argument<FilePath>("CodeCoverageReportPath", "coverage.zip");
var packageVersion = "0.1.0";
var packageOutputPath = Argument<DirectoryPath>("PackageOutputPath", "packages");

Task("Restore")
    .Does(() => 
    {
        NuGetRestore(Paths.SolutionFile);
    });

Task("Remove-Packages")
    .Does(() =>
    {
        CleanDirectory(packageOutputPath);
    });

Task("Package-NuGet")
    .IsDependentOn("Test")
    .IsDependentOn("Version")
    .IsDependentOn("Remove-Packages")
    .Does(() =>
    {
        EnsureDirectoryExists(packageOutputPath);
        NuGetPack(Paths.WebNuspecFile,
            new NuGetPackSettings
            {
                Version = packageVersion, 
                OutputDirectory = packageOutputPath, 
                NoPackageAnalysis = true
            });
    });

Task("Build")
    .IsDependentOn("Restore")
    .Does(() =>
    {
        DotNetBuild(Paths.SolutionFile, 
                    settings => settings
                                    .SetConfiguration(configuration)
                                    .WithTarget("Build"));
    });

Task("Test")
    .IsDependentOn("Build")
    .Does(() => 
    {
        OpenCover(tool => tool.XUnit2($"**/bin/{configuration}/*Tests.dll", 
            new XUnit2Settings()
            {
                ShadowCopy = false
            }),
        Paths.CodeCoverageResultFile, 
        new OpenCoverSettings()
            .WithFilter($"+[{Linker}.*]*")
            .WithFilter($"-[{Linker}.*Tests*]*")
            );
    });
    
Task("Report-Coverage")
    .IsDependentOn("Test")
    .Does(() => 
    {
        ReportGenerator(Paths.CodeCoverageResultFile, Paths.CodeCoverageReportDirectory, 
        new ReportGeneratorSettings
        {
            ReportTypes = new[] {ReportGeneratorReportType.Html }
        });
        Zip(Paths.CodeCoverageReportDirectory, MakeAbsolute(codeCoverageReportPath));
    });

Task("Version")
    .Does(() => 
    {
        var version = GitVersion();
        packageVersion = version.NuGetVersion;
        if(!BuildSystem.IsLocalBuild)
        {
            GitVersion(new GitVersionSettings
            {
                OutputType = GitVersionOutput.BuildServer, //makes the version the build id or part of the metadata on the CI server
                UpdateAssemblyInfo = true
            });
        }
    });

Task("Deploy-OctopusDeploy")
    .IsDependentOn("Package-Nuget")
    .Does(() => 
    {
        //upload packages
        OctoPush(Urls.OctopusServerUrl, 
                EnvironmentVariable("OctopusApiKey"),
                GetFiles($"{packageOutputPath}/*.nupkg"), 
                new OctopusPushSettings
                {
                    ReplaceExisting = true
                });

        //create release
        OctoCreateRelease(projectName, 
        new CreateReleaseSettings
        {
            Server = Urls.OctopusServerUrl, 
            ApiKey = EnvironmentVariable("OctopusApiKey"), 
            ReleaseNumber = packageVersion, 
            DefaultPackageVersion = packageVersion,
            DeployTo = "Development", 
            WaitForDeployment = true
        });
    });

RunTarget(target);
