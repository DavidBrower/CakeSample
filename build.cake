#load build/paths.cake
#load build/urls.cake

#tool nuget:?package=MSTest.TestFramework&version=1.2.0
#tool nuget:?package=OpenCover&version=4.6.519
#tool nuget:?package=ReportGenerator&version=2.5.8
#tool nuget:?package=GitVersion.CommandLine
#tool nuget:?package=OctopusTools&version=4.27.3
#tool nuget:?package=xunit.runner.console&version=2.3.1
#tool nuget:?package=OpenCoverToCoberturaConverter&version=0.3.2

#addin nuget:?package=Cake.Yaml
#addin nuget:?package=YamlDotNet

var projectName            = ""; //provide the name of the Octopus Deploy project
var target                 = Argument<string>("Target", "Report-Coverage");
var buildConfiguration     = Argument<string>("Configuration", "Release");
var codeCoverageReportPath = Argument<FilePath>("CodeCoverageReportPath", "coverage.zip");
var packageVersion         = "0.1.0";
var packageOutputPath      = Argument<DirectoryPath>("PackageOutputPath", "packages");
var configurationFile      = "build/config.yml";
var excludeTraits          = Argument<String[]>("ExcludeTraits", new []{"Integration"});

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
		Information($"Building with configuration: {buildConfiguration}");
		
        MSBuild(Paths.SolutionFile, 
                new MSBuildSettings {
                    Configuration = buildConfiguration
                }
                );
    });

Task("Test")
    .IsDependentOn("Build")
    .Does(() => 
    {
		//Populate Configuration class from YAML file
		var configuration = DeserializeYamlFromFile<Configuration>(configurationFile);

        var xunitSettings = new XUnit2Settings() {ShadowCopy = false};
        
        foreach(var trait in excludeTraits)
            xunitSettings.ExcludeTrait("Category", trait);
			
		//Only create test artifact file when running on build server
		if(IsServerBuild)
		{
			//Run XUnit tests for configured locations	
			xunitSettings.OutputDirectory = "./XUnitTestResults";
            xunitSettings.XmlReport       = true;
            xunitSettings.ReportName      = "Results";
		}
		
		var openCoverSettings = new OpenCoverSettings()
					.WithFilter($"+[*]*")
					.WithFilter($"-[*Test*]*");
					
		openCoverSettings.MergeByHash = true; 
		
		OpenCover(tool => tool.XUnit2(GetProjectLocations(configuration.TestProjects), 
            xunitSettings),
				Paths.OpenCoverResultFile, 
				openCoverSettings
            );
    });
	
private bool IsServerBuild => !BuildSystem.IsLocalBuild; 
    
private IEnumerable<string> GetProjectLocations(IEnumerable<string> projects) => 
		projects.Select(p => p.Replace("{configuration}", buildConfiguration));

Task("Report-Coverage")
    .IsDependentOn("Test")
    .Does(() => 
    {
		StartProcess(Paths.OpenCoverToCobertura, 
			new ProcessSettings {
				Arguments = new ProcessArgumentBuilder()
					.Append($"-input:{Paths.OpenCoverResultFile}")
					.Append($"-output:{Paths.CoberturaResultFile}")
		});
		
        ReportGenerator(Paths.OpenCoverResultFile, Paths.CodeCoverageReportDirectory, 
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

//Configuration class to hold properties from YAML file
public class Configuration
{
	public List<string> TestProjects {get; set;}
}
	
	
