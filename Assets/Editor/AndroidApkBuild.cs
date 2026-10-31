using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace WhaleMovie.BuildTools
{
    public static class AndroidApkBuild
    {
        public static void Build()
        {
            string[] args = Environment.GetCommandLineArgs();
            int outputIndex = Array.IndexOf(args, "-apkOutput");
            if (outputIndex < 0 || outputIndex + 1 >= args.Length)
                throw new ArgumentException("Provide -apkOutput followed by the APK output path.");

            string outputPath = Path.GetFullPath(args[outputIndex + 1]);
            if (!string.Equals(Path.GetExtension(outputPath), ".apk", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("The output path must end with .apk.");

            string[] scenes = EditorBuildSettings.scenes
                .Where(scene => scene.enabled)
                .Select(scene => scene.path)
                .ToArray();
            if (scenes.Length == 0)
                throw new InvalidOperationException("No scenes are enabled in Build Settings.");

            Directory.CreateDirectory(Path.GetDirectoryName(outputPath));
            bool previousBundleSetting = EditorUserBuildSettings.buildAppBundle;
            bool previousExportSetting = EditorUserBuildSettings.exportAsGoogleAndroidProject;
            string previousBuildLocation = EditorUserBuildSettings.GetBuildLocation(BuildTarget.Android);
            try
            {
                EditorUserBuildSettings.buildAppBundle = false;
                EditorUserBuildSettings.exportAsGoogleAndroidProject = false;
                Debug.Log("[AndroidApkBuild] Building " + scenes.Length + " enabled scenes to " + outputPath);
                BuildReport report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
                {
                    scenes = scenes,
                    locationPathName = outputPath,
                    target = BuildTarget.Android,
                    options = BuildOptions.CompressWithLz4
                });

                if (report.summary.result != BuildResult.Succeeded)
                    throw new InvalidOperationException("APK build failed: " + report.summary.result
                        + ", errors=" + report.summary.totalErrors);

                Debug.Log("[AndroidApkBuild] SUCCESS path=" + report.summary.outputPath
                    + " bytes=" + report.summary.totalSize
                    + " duration=" + report.summary.totalTime);
            }
            finally
            {
                EditorUserBuildSettings.buildAppBundle = previousBundleSetting;
                EditorUserBuildSettings.exportAsGoogleAndroidProject = previousExportSetting;
                EditorUserBuildSettings.SetBuildLocation(BuildTarget.Android, previousBuildLocation);
            }
        }
    }
}
