using UnityEditor;
using UnityEngine;

namespace HideAndShoot.EditorTools
{
    /// <summary>
    /// Import profiles for curated runtime sources; raw source archives stay outside Assets.
    /// Note: 3D arms extraction / retargeting still requires in-editor visual validation.
    /// </summary>
    internal sealed class HideAndShootAssetImporter : AssetPostprocessor
    {
        private const string Root = "Assets/HideAndShoot/ThirdParty/";

        void OnPreprocessModel()
        {
            if (!assetPath.StartsWith(Root, System.StringComparison.Ordinal)) return;
            var model = (ModelImporter)assetImporter;
            model.importCameras = false;
            model.importLights = false;
            model.isReadable = false;
            if (assetPath.Contains("/RobotKyle/") || assetPath.Contains("/Mixamo/"))
            {
                model.animationType = ModelImporterAnimationType.Human;
                model.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                model.importAnimation = true;
                // Keep source bone hierarchy intact for Animator/right-hand pistol attachment.
                model.optimizeGameObjects = false;
                // Editor-only first-person Kyle arms extraction needs source mesh triangles.
                if (assetPath.Contains("/RobotKyle/"))
                    model.isReadable = true;
            }
            else
            {
                model.importAnimation = false;
            }
        }

        void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith(Root, System.StringComparison.Ordinal)) return;
            var texture = (TextureImporter)assetImporter;
            texture.textureType = TextureImporterType.Default;
            texture.sRGBTexture = true;
            if (assetPath.Contains("/AmmoPickup/") || assetPath.Contains("/Kenney/"))
            {
                texture.filterMode = FilterMode.Point;
                texture.mipmapEnabled = false;
                texture.textureCompression = TextureImporterCompression.Uncompressed;
            }
            if (assetPath.Contains("/RobotKyle/"))
            {
                texture.mipmapEnabled = true;
                texture.filterMode = FilterMode.Bilinear;
                if (assetPath.Contains("Robot_Normal"))
                    texture.textureType = TextureImporterType.NormalMap;
            }
        }

        void OnPreprocessAudio()
        {
            if (!assetPath.StartsWith(Root + "Audio/", System.StringComparison.Ordinal)) return;
            var audio = (AudioImporter)assetImporter;
            audio.loadInBackground = false;
            audio.defaultSampleSettings = new AudioImporterSampleSettings
            {
                loadType = AudioClipLoadType.DecompressOnLoad,
                compressionFormat = AudioCompressionFormat.PCM,
                quality = 1f,
                sampleRateSetting = AudioSampleRateSetting.PreserveSampleRate
            };
        }
    }
}
