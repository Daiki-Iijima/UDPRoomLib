#if UNITY_IOS
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.iOS.Xcode;
using System.IO;

public static class AutoCodeSign
{
    [PostProcessBuild]
    public static void OnPostprocessBuild(BuildTarget buildTarget, string pathToBuildProject)
    {
        if (buildTarget != BuildTarget.iOS)
            return;

        const string teamID = "49BQT24CU7";  // ← Apple Developer チームIDに置き換えて
        const string provisioningStyle = "Automatic"; // or "Manual"
        // const string bundleID = "com.yourcompany.yourapp"; // ← 任意に置き換え（必要なら）

        // Unity-iPhoneプロジェクトファイルのパス
        string pbxProjectPath = PBXProject.GetPBXProjectPath(pathToBuildProject);
        PBXProject proj = new PBXProject();
        proj.ReadFromFile(pbxProjectPath);

        string targetGUID = proj.GetUnityMainTargetGuid();
        string frameworkGUID = proj.GetUnityFrameworkTargetGuid();

        // チームID設定
        proj.SetBuildProperty(targetGUID, "DEVELOPMENT_TEAM", teamID);
        proj.SetBuildProperty(frameworkGUID, "DEVELOPMENT_TEAM", teamID);

        // 自動署名を有効化
        proj.SetBuildProperty(targetGUID, "CODE_SIGN_STYLE", provisioningStyle);
        proj.SetBuildProperty(frameworkGUID, "CODE_SIGN_STYLE", provisioningStyle);

        // Bundle IDの上書き（必要なら）
        // proj.SetBuildProperty(targetGUID, "PRODUCT_BUNDLE_IDENTIFIER", bundleID);

        // 書き戻す
        proj.WriteToFile(pbxProjectPath);
    }
}
#endif
