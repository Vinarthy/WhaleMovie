using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace WhaleMovie.Art.Editor
{
    // Creates an editable prefab and its source textures, meshes and materials.
    public static class BlueVortexPortalBuilder
    {
        private const string AssetRoot = "Assets/Resources/Material/Portal/Generated";
        private const string PrefabPath = "Assets/Resources/Prefeb/BlueVortexPortal.prefab";

        [MenuItem("Tools/WhaleMovie/Generate Blue Vortex Portal")]
        public static void Generate()
        {
            Directory.CreateDirectory(AssetRoot);
            AssetDatabase.Refresh();

            Texture2D noiseA = WriteTexture("Portal_FlowNoise_A.png", 512, 512, MakeNoise(512, 0.17f), true);
            Texture2D noiseB = WriteTexture("Portal_FlowNoise_B.png", 512, 512, MakeNoise(512, 13.37f), true);
            Texture2D ringMask = WriteTexture("Portal_RingMask.png", 512, 128, MakeRingMask(512, 128), true);
            Texture2D spark = WriteTexture("Portal_BlueSpark.png", 128, 128, MakeSpark(128), false);

            Shader coreShader = Shader.Find("WhaleMovie/Portal/Blue Vortex Core");
            Shader additiveShader = Shader.Find("WhaleMovie/Portal/Blue Vortex Additive");
            Shader litShader = Shader.Find("Universal Render Pipeline/Lit");
            if (coreShader == null || additiveShader == null || litShader == null)
                throw new InvalidOperationException("Blue Vortex Portal shaders were not imported.");

            Material core = SaveMaterial("Portal_Core.mat", coreShader);
            core.SetTexture("_NoiseA", noiseA);
            core.SetTexture("_NoiseB", noiseB);
            core.SetColor("_DarkColor", new Color(0.006f, 0.012f, 0.045f));
            core.SetColor("_BlueColor", new Color(0.025f, 0.35f, 1.4f));
            core.SetColor("_WhiteColor", new Color(1.15f, 1.25f, 1.35f));
            core.SetFloat("_CoreBrightness", 0.85f);
            core.SetFloat("_SpinSpeed", 0.16f);
            core.SetFloat("_Swirl", 1.7f);

            Material rim = SaveMaterial("Portal_Rim.mat", litShader);
            rim.SetColor("_BaseColor", new Color(0.075f, 0.36f, 0.72f));
            rim.SetFloat("_Metallic", 0.68f);
            rim.SetFloat("_Smoothness", 0.72f);
            rim.EnableKeyword("_EMISSION");
            rim.SetColor("_EmissionColor", new Color(0.16f, 1.15f, 2.4f));

            Material ringGlow = SaveMaterial("Portal_RingGlow.mat", additiveShader);
            ringGlow.SetTexture("_MainTex", ringMask);
            ringGlow.SetColor("_Tint", new Color(0.2f, 1.35f, 3.8f, 0.95f));
            ringGlow.SetFloat("_ScrollSpeed", 0.11f);

            Material sparkGlow = SaveMaterial("Portal_BlueParticles.mat", additiveShader);
            sparkGlow.SetTexture("_MainTex", spark);
            sparkGlow.SetColor("_Tint", new Color(0.35f, 1.35f, 3.2f, 0.86f));
            sparkGlow.SetFloat("_ScrollSpeed", 0f);

            Mesh disc = SaveMesh("Portal_Disc.asset", BuildDisc(0.83f, 96));
            Mesh rimMesh = SaveMesh("Portal_RimMesh.asset", BuildTorus(0.92f, 0.09f, 96, 16));
            Mesh glowMesh = SaveMesh("Portal_GlowMesh.asset", BuildTorus(0.92f, 0.125f, 96, 16));

            GameObject root = new GameObject("Blue Vortex Portal");
            try
            {
                CreateMeshChild(root.transform, "01_Opaque_BlackWhiteBlue_Vortex", disc, core, Vector3.zero);
                CreateMeshChild(root.transform, "02_Dark_Rim", rimMesh, rim, new Vector3(0, 0, 0.005f));
                CreateMeshChild(root.transform, "03_Blue_Glowing_Rim", glowMesh, ringGlow, new Vector3(0, 0, 0.007f));
                CreateParticles(root.transform, "04_Fine_Blue_Sparks", sparkGlow, 48f, 190, 0.025f, 0.075f, 0.08f, 0.26f);
                CreateParticles(root.transform, "05_Slow_Blue_Wisps", sparkGlow, 11f, 55, 0.09f, 0.17f, 0.03f, 0.11f);

                GameObject lightObject = new GameObject("06_Subtle_Blue_Light");
                lightObject.transform.SetParent(root.transform, false);
                Light light = lightObject.AddComponent<Light>();
                light.type = LightType.Point;
                light.color = new Color(0.12f, 0.62f, 1f);
                light.intensity = 0.65f;
                light.range = 2.7f;
                light.shadows = LightShadows.None;

                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"Blue Vortex Portal prefab generated: {PrefabPath}");
        }

        private static Material SaveMaterial(string name, Shader shader)
        {
            string path = AssetRoot + "/" + name;
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(shader) { name = Path.GetFileNameWithoutExtension(name) };
                AssetDatabase.CreateAsset(material, path);
            }
            else
            {
                material.shader = shader;
            }
            EditorUtility.SetDirty(material);
            return material;
        }

        private static Mesh SaveMesh(string name, Mesh source)
        {
            string path = AssetRoot + "/" + name;
            Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (mesh == null)
            {
                source.name = Path.GetFileNameWithoutExtension(name);
                AssetDatabase.CreateAsset(source, path);
                return source;
            }
            EditorUtility.CopySerialized(source, mesh);
            UnityEngine.Object.DestroyImmediate(source);
            EditorUtility.SetDirty(mesh);
            return mesh;
        }

        private static Texture2D WriteTexture(string name, int width, int height, Color32[] pixels, bool repeat)
        {
            string path = AssetRoot + "/" + name;
            Texture2D texture = new Texture2D(width, height, TextureFormat.RGBA32, false, true);
            texture.SetPixels32(pixels);
            texture.Apply(false);
            File.WriteAllBytes(path, texture.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);

            TextureImporter importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Default;
            importer.sRGBTexture = !repeat;
            importer.alphaIsTransparency = !repeat;
            importer.wrapMode = repeat ? TextureWrapMode.Repeat : TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            importer.mipmapEnabled = repeat;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        private static Color32[] MakeNoise(int size, float offset)
        {
            Color32[] result = new Color32[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float u = x / (float)(size - 1);
                float v = y / (float)(size - 1);
                float n = 0.52f * TileNoise(u, v, 5, offset)
                        + 0.31f * TileNoise(u, v, 11, offset + 9.7f)
                        + 0.17f * TileNoise(u, v, 23, offset + 18.1f);
                byte value = (byte)Mathf.RoundToInt(Mathf.Clamp01(n) * 255f);
                result[y * size + x] = new Color32(value, value, value, 255);
            }
            return result;
        }

        private static float TileNoise(float u, float v, int period, float offset)
        {
            float x = u * period;
            float y = v * period;
            float a = Mathf.PerlinNoise(x + offset, y + offset);
            float b = Mathf.PerlinNoise(x - period + offset, y + offset);
            float c = Mathf.PerlinNoise(x + offset, y - period + offset);
            float d = Mathf.PerlinNoise(x - period + offset, y - period + offset);
            return Mathf.Lerp(Mathf.Lerp(a, b, u), Mathf.Lerp(c, d, u), v);
        }

        private static Color32[] MakeRingMask(int width, int height)
        {
            Color32[] result = new Color32[width * height];
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                float u = x / (float)(width - 1);
                float v = y / (float)(height - 1);
                float arcs = Mathf.Pow(Mathf.Clamp01(TileNoise(u, v, 18, 3.1f) * 1.7f - 0.45f), 2f);
                float pulse = 0.45f + 0.55f * Mathf.Pow(Mathf.Max(0, Mathf.Sin(u * Mathf.PI * 16f)), 6f);
                float front = 0.25f + 0.75f * Mathf.Max(0, Mathf.Sin(v * Mathf.PI * 2f));
                byte alpha = (byte)Mathf.RoundToInt(Mathf.Clamp01((0.25f + arcs * pulse) * front) * 255f);
                result[y * width + x] = new Color32(255, 255, 255, alpha);
            }
            return result;
        }

        private static Color32[] MakeSpark(int size)
        {
            Color32[] result = new Color32[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float px = (x + 0.5f - size * 0.5f) / (size * 0.5f);
                float py = (y + 0.5f - size * 0.5f) / (size * 0.5f);
                float r2 = px * px + py * py;
                float glow = Mathf.Exp(-r2 * 8f);
                float cross = Mathf.Exp(-px * px * 100f - py * py * 6f)
                            + Mathf.Exp(-py * py * 100f - px * px * 6f);
                byte alpha = (byte)Mathf.RoundToInt(Mathf.Clamp01(glow * 0.76f + cross * 0.24f) * 255f);
                result[y * size + x] = new Color32(255, 255, 255, alpha);
            }
            return result;
        }

        private static Mesh BuildDisc(float radius, int segments)
        {
            Vector3[] vertices = new Vector3[segments + 2];
            Vector2[] uv = new Vector2[vertices.Length];
            int[] triangles = new int[segments * 3];
            uv[0] = new Vector2(0.5f, 0.5f);
            for (int i = 0; i <= segments; i++)
            {
                float a = i * Mathf.PI * 2f / segments;
                float c = Mathf.Cos(a), s = Mathf.Sin(a);
                vertices[i + 1] = new Vector3(c * radius, s * radius, 0f);
                uv[i + 1] = new Vector2(0.5f + c * 0.5f, 0.5f + s * 0.5f);
                if (i == segments) continue;
                triangles[i * 3] = 0;
                triangles[i * 3 + 1] = i + 1;
                triangles[i * 3 + 2] = i + 2;
            }
            Mesh mesh = new Mesh { vertices = vertices, uv = uv, triangles = triangles };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Mesh BuildTorus(float major, float minor, int around, int across)
        {
            Vector3[] vertices = new Vector3[(around + 1) * (across + 1)];
            Vector2[] uv = new Vector2[vertices.Length];
            int[] triangles = new int[around * across * 6];
            for (int i = 0; i <= around; i++)
            for (int j = 0; j <= across; j++)
            {
                float a = i * Mathf.PI * 2f / around;
                float b = j * Mathf.PI * 2f / across;
                Vector3 radial = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f);
                int index = i * (across + 1) + j;
                vertices[index] = radial * (major + minor * Mathf.Cos(b)) + Vector3.forward * (minor * Mathf.Sin(b));
                uv[index] = new Vector2(i / (float)around, j / (float)across);
                if (i == around || j == across) continue;
                int next = (i + 1) * (across + 1) + j;
                int offset = (i * across + j) * 6;
                triangles[offset] = index;
                triangles[offset + 1] = next;
                triangles[offset + 2] = index + 1;
                triangles[offset + 3] = next;
                triangles[offset + 4] = next + 1;
                triangles[offset + 5] = index + 1;
            }
            Mesh mesh = new Mesh { vertices = vertices, uv = uv, triangles = triangles };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static void CreateMeshChild(Transform parent, string name, Mesh mesh, Material material, Vector3 position)
        {
            GameObject child = new GameObject(name);
            child.transform.SetParent(parent, false);
            child.transform.localPosition = position;
            child.AddComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer renderer = child.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        }

        private static void CreateParticles(Transform parent, string name, Material material,
            float rate, int max, float minSize, float maxSize, float minSpeed, float maxSpeed)
        {
            GameObject child = new GameObject(name);
            child.transform.SetParent(parent, false);
            ParticleSystem particles = child.AddComponent<ParticleSystem>();
            var main = particles.main;
            main.loop = true;
            main.playOnAwake = true;
            main.duration = 5f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.85f, 1.6f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(minSpeed, maxSpeed);
            main.startSize = new ParticleSystem.MinMaxCurve(minSize, maxSize);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.maxParticles = max;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.startColor = Color.white;

            var emission = particles.emission;
            emission.rateOverTime = rate;
            var shape = particles.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = 0.93f;
            shape.radiusThickness = 0f;
            shape.randomDirectionAmount = 0.45f;

            Gradient fade = new Gradient();
            fade.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.18f),
                        new GradientAlphaKey(0.65f, 0.7f), new GradientAlphaKey(0f, 1f) });
            var color = particles.colorOverLifetime;
            color.enabled = true;
            color.color = new ParticleSystem.MinMaxGradient(fade);

            ParticleSystemRenderer renderer = child.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.sortingOrder = 5;
        }
    }
}
