// --------------------------------------------------------------------------------------
// FAKE build script
// --------------------------------------------------------------------------------------

#r @"packages/build/FAKE/tools/FakeLib.dll"
open Fake
open Fake.Git
open Fake.AssemblyInfoFile
open Fake.ReleaseNotesHelper
open Fake.UserInputHelper
open System
open System.IO
#if MONO
#else
#load "packages/build/SourceLink.Fake/tools/Fake.fsx"
open SourceLink
#endif

let wc = new System.Net.WebClient()

// --------------------------------------------------------------------------------------
// START TODO: Provide project-specific details below
// --------------------------------------------------------------------------------------

// Information about the project are used
//  - for version and project name in generated AssemblyInfo file
//  - by the generated NuGet package
//  - to run tests and to publish documentation on GitHub gh-pages
//  - for documentation, you also need to edit info in "docs/tools/generate.fsx"

// The name of the project
// (used by attributes in AssemblyInfo, name of a NuGet package and directory in 'src')
let project = "caffe.clr.demo"

// Short summary of the project
// (used as description in AssemblyInfo and as a short summary for NuGet package)
let summary = "A demo and tutorial of how to use caffe.clr"

// Longer description of the project
// (used as a description for NuGet package; line breaks are automatically cleaned up)
let description = "A demo and tutorial of how to use caffe.clr"

// List of author names (for NuGet package)
let authors = [ "Robert Pickering" ]

// Tags for your project (for NuGet package)
let tags = "caffe neural-networks deep-dream"

// File system information
let solutionFile  = "caffe.clr.demo.sln"

// Default target configuration
let configuration = "Release"

// Pattern specifying assemblies to be tested using NUnit
let testAssemblies = "tests/**/bin" </> configuration </> "*Tests*.dll"

// Git configuration (used for publishing documentation in gh-pages branch)
// The profile where the project is posted
let gitOwner = "robertpi"
let gitHome = sprintf "%s/%s" "https://github.com" gitOwner

// The name of the project on GitHub
let gitName = "caffe.clr.demo"

// The url for the raw files hosted
let gitRaw = environVarOrDefault "gitRaw" "https://raw.githubusercontent.com/robertpi"

// --------------------------------------------------------------------------------------
// END TODO: The rest of the file includes standard build steps
// --------------------------------------------------------------------------------------

// Read additional information from the release notes document
let release = LoadReleaseNotes "RELEASE_NOTES.md"

// Helper active pattern for project types
let (|Fsproj|Csproj|Vbproj|Shproj|) (projFileName:string) =
    match projFileName with
    | f when f.EndsWith("fsproj") -> Fsproj
    | f when f.EndsWith("csproj") -> Csproj
    | f when f.EndsWith("vbproj") -> Vbproj
    | f when f.EndsWith("shproj") -> Shproj
    | _                           -> failwith (sprintf "Project file %s not supported. Unknown project type." projFileName)

// Generate assembly info files with the right version & up-to-date information
Target "AssemblyInfo" (fun _ ->
    let getAssemblyInfoAttributes projectName =
        [ Attribute.Title (projectName)
          Attribute.Product project
          Attribute.Description summary
          Attribute.Version release.AssemblyVersion
          Attribute.FileVersion release.AssemblyVersion
          Attribute.Configuration configuration ]

    let getProjectDetails projectPath =
        let projectName = System.IO.Path.GetFileNameWithoutExtension(projectPath)
        ( projectPath,
          projectName,
          System.IO.Path.GetDirectoryName(projectPath),
          (getAssemblyInfoAttributes projectName)
        )

    !! "src/**/*.??proj"
    |> Seq.map getProjectDetails
    |> Seq.iter (fun (projFileName, projectName, folderName, attributes) ->
        match projFileName with
        | Fsproj -> CreateFSharpAssemblyInfo (folderName </> "AssemblyInfo.fs") attributes
        | Csproj -> CreateCSharpAssemblyInfo ((folderName </> "Properties") </> "AssemblyInfo.cs") attributes
        | Vbproj -> CreateVisualBasicAssemblyInfo ((folderName </> "My Project") </> "AssemblyInfo.vb") attributes
        | Shproj -> ()
        )
)

let projects =
    !! "src/**/*.??proj"
    -- "src/**/*.shproj"

let projectOutputFromProjFile file =
    "bin" </> (System.IO.Path.GetFileNameWithoutExtension file)

// Copies binaries from default VS location to expected bin folder
// But keeps a subdirectory structure for each project in the
// src folder to support multiple project outputs
Target "CopyBinaries" (fun _ ->
    projects
    |>  Seq.map (fun f -> ((System.IO.Path.GetDirectoryName f) </> "bin" </> configuration, projectOutputFromProjFile f))
    |>  Seq.iter (fun (fromDir, toDir) -> CopyDir toDir fromDir (fun _ -> true))
)

// --------------------------------------------------------------------------------------
// Clean build results

let vsProjProps = 
#if MONO
    [ ("DefineConstants","MONO"); ("Configuration", configuration) ]
#else
    [ ("Configuration", configuration); ("Platform", "Any CPU") ]
#endif

Target "Clean" (fun _ ->
    !! solutionFile |> MSBuildReleaseExt "" vsProjProps "Clean" |> ignore
    CleanDirs ["bin"; "temp"; "docs/output"]
)

// --------------------------------------------------------------------------------------
// Build library & test project

Target "Build" (fun _ ->
    !! solutionFile
    |> MSBuildReleaseExt "" vsProjProps "Rebuild"
    |> ignore
)

// --------------------------------------------------------------------------------------
// Deploy native files

let nativeLocations =
    [ "packages/caffe.clr/lib/net452" // I keep the native and clr dll in the same dir, yes this was a mistake
      "packages/boost_date_time-vc120/lib/native/address-model-64/lib";
      "packages/gflags/build/native/x64/v120/dynamic/Lib";
      "packages/hdf5-v120-complete/lib/native/bin/x64";
      "packages/lmdb-v120-clean/lib/native/bin/x64";
      "packages/OpenBLAS/lib/native/bin/x64";
      "lib/OpenCV"; 
      "lib/glog"; 
      "lib/gflags"; 
      "lib/cudnn"; ]

let dllOrPdb (file: string) = 
    file.EndsWith ".dll" || file.EndsWith ".pdb" 

Target "CopyNativeDependencies" (fun _ ->
    nativeLocations
    |> Seq.iter (fun dir -> projects |> Seq.iter (fun proj ->  CopyDir (projectOutputFromProjFile proj) dir dllOrPdb))
)


Target "CopyNativeDependenciesLocal" (fun _ ->
    let buildDir = getBuildParamOrDefault "builddir" "./unknown"
    nativeLocations
    |> Seq.iter (fun dir -> projects |> Seq.iter (fun proj ->  CopyDir buildDir dir dllOrPdb))
)

// --------------------------------------------------------------------------------------
// Download models

let makeUrl = sprintf "http://dl.caffe.berkeleyvision.org/%s.caffemodel"

Target "DownloadModels" (fun _ ->
    Directory.GetDirectories("models")
    |> Seq.iter (fun dir ->
        let modelName = Path.GetFileName dir
        logfn "Downloading %s ..." modelName
        wc.DownloadFile(makeUrl modelName, sprintf @"models\%s\%s.caffemodel" modelName modelName)
        logfn "Finished Downloading %s" modelName)
)

// --------------------------------------------------------------------------------------
// Download data

Target "DownloadData" (fun _ ->
    ensureDirectory @"data\ilsvrc12"
    logfn "Downloading data ..."
    wc.DownloadFile("http://dl.caffe.berkeleyvision.org/caffe_ilsvrc12.tar.gz", @"data\ilsvrc12\caffe_ilsvrc12.tar.gz")
    logfn "Unzipping data ..."
    ArchiveHelper.Tar.GZip.Extract (directoryInfo @"data\ilsvrc12") (fileInfo @"data\ilsvrc12\caffe_ilsvrc12.tar.gz")
    logfn "Finished Downloading"
)

// --------------------------------------------------------------------------------------
// Execute

let execProject path args =
    ExecProcess (fun info ->
        info.FileName <- path
        info.WorkingDirectory <- Path.GetDirectoryName path
        info.Arguments <- args)
        (TimeSpan.FromMinutes 10.)

// --------------------------------------------------------------------------------------
// Execute classification

let classificationArgs = @"..\..\models\bvlc_reference_caffenet\deploy.prototxt ..\..\models\bvlc_reference_caffenet\bvlc_reference_caffenet.caffemodel ..\..\data\ilsvrc12\imagenet_mean.binaryproto ..\..\data\ilsvrc12\synset_words.txt ..\..\data\maxcat.jpg"

Target "ExecuteClassification" (fun _ ->
    let res = execProject @"bin\classification\classification.exe" classificationArgs
    if res <> 0 then failwith "non-zero exit code"
)

// --------------------------------------------------------------------------------------
// Execute deep dream

let deepDreamArgs = @"..\..\models\bvlc_googlenet\deploy.prototxt ..\..\models\bvlc_googlenet\bvlc_googlenet.caffemodel ..\..\data\ilsvrc12\imagenet_mean.binaryproto ..\..\data\maxcat.jpg"

Target "ExecuteDeepDream" (fun _ ->
    let res = execProject @"bin\deepdream\deepdream.exe" deepDreamArgs
    if res <> 0 then failwith "non-zero exit code"
)

#if MONO
#else
// --------------------------------------------------------------------------------------
// SourceLink allows Source Indexing on the PDB generated by the compiler, this allows
// the ability to step through the source code of external libraries http://ctaggart.github.io/SourceLink/

Target "SourceLink" (fun _ ->
    let baseUrl = sprintf "%s/%s/{0}/%%var2%%" gitRaw project
    !! "src/**/*.??proj"
    -- "src/**/*.shproj"
    |> Seq.iter (fun projFile ->
        let proj = VsProj.LoadRelease projFile
        SourceLink.Index proj.CompilesNotLinked proj.OutputFilePdb __SOURCE_DIRECTORY__ baseUrl
    )
)

#endif


// --------------------------------------------------------------------------------------
// Run all targets by default. Invoke 'build <Target>' to override

Target "All" DoNothing

"AssemblyInfo"
  ==> "Build"
  ==> "CopyBinaries"
  ==> "CopyNativeDependencies"
#if MONO
#else
  =?> ("SourceLink", Pdbstr.tryFind().IsSome )
#endif
  ==> "All"

"All"
  ==> "ExecuteClassification"

"All"
  ==> "ExecuteDeepDream"

RunTargetOrDefault "All"
