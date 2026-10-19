// Builds the reusable 3D preview stage: lighting, ground, pedestal and orbit camera.
// Kept separate from the preview UI/controller so other examples can reuse the same stage.
using Godot;

namespace PixelCreatures.Examples
{
    public static class PreviewStageBuilder
    {
        public static (Node3D ModelRoot, Node3D CameraRig, Camera3D Camera) Build(Node3D owner)
        {
            var world = new WorldEnvironment();
            var environment = new Godot.Environment
            {
                BackgroundMode = Godot.Environment.BGMode.Color,
                BackgroundColor = new Color(0.055f, 0.067f, 0.09f),
                AmbientLightSource = Godot.Environment.AmbientSource.Color,
                AmbientLightColor = new Color(0.57f, 0.62f, 0.72f),
                AmbientLightEnergy = 0.72f,
                TonemapMode = Godot.Environment.ToneMapper.Filmic,
                GlowEnabled = true,
                GlowIntensity = 0.35f,
            };
            world.Environment = environment;
            owner.AddChild(world);

            owner.AddChild(new DirectionalLight3D
            {
                Name = "KeyLight",
                RotationDegrees = new Vector3(-43, -32, 0),
                LightColor = new Color(1.0f, 0.91f, 0.78f),
                LightEnergy = 1.25f,
                ShadowEnabled = true,
                ShadowOpacity = 0.38f,
            });
            owner.AddChild(new DirectionalLight3D
            {
                Name = "FillLight",
                RotationDegrees = new Vector3(-20, 145, 0),
                LightColor = new Color(0.46f, 0.68f, 1.0f),
                LightEnergy = 0.62f,
                ShadowEnabled = false,
            });

            owner.AddChild(new MeshInstance3D
            {
                Name = "PreviewFloor",
                Mesh = new PlaneMesh { Size = new Vector2(80, 80) },
                MaterialOverride = new StandardMaterial3D
                {
                    AlbedoColor = new Color(0.075f, 0.09f, 0.12f),
                    Roughness = 0.92f,
                },
                Position = new Vector3(0, -0.16f, 0),
            });

            var gridMaterial = new StandardMaterial3D
            {
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                AlbedoColor = new Color(0.18f, 0.22f, 0.29f, 0.58f),
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            };
            var gridMesh = new ImmediateMesh();
            gridMesh.SurfaceBegin(Mesh.PrimitiveType.Lines, gridMaterial);
            for (int i = -12; i <= 12; i++)
            {
                float p = i * 0.75f;
                gridMesh.SurfaceAddVertex(new Vector3(p, -0.155f, -9));
                gridMesh.SurfaceAddVertex(new Vector3(p, -0.155f, 9));
                gridMesh.SurfaceAddVertex(new Vector3(-9, -0.155f, p));
                gridMesh.SurfaceAddVertex(new Vector3(9, -0.155f, p));
            }
            gridMesh.SurfaceEnd();
            owner.AddChild(new MeshInstance3D { Name = "PreviewGrid", Mesh = gridMesh });

            owner.AddChild(new MeshInstance3D
            {
                Name = "DisplayPedestal",
                Mesh = new CylinderMesh
                {
                    TopRadius = 1.05f,
                    BottomRadius = 1.18f,
                    Height = 0.16f,
                    RadialSegments = 48,
                },
                Position = new Vector3(0, -0.08f, 0),
                MaterialOverride = new StandardMaterial3D
                {
                    AlbedoColor = new Color(0.15f, 0.18f, 0.23f),
                    Metallic = 0.38f,
                    Roughness = 0.42f,
                },
            });

            var modelRoot = new Node3D { Name = "GeneratedCreature" };
            owner.AddChild(modelRoot);

            var cameraRig = new Node3D { Name = "OrbitRig", Position = new Vector3(0, 1.0f, 0) };
            owner.AddChild(cameraRig);
            var camera = new Camera3D
            {
                Name = "PreviewCamera",
                Position = new Vector3(0, 0, 6.5f),
                Current = true,
                Fov = 42,
                Near = 0.03f,
                Far = 100,
            };
            cameraRig.AddChild(camera);
            return (modelRoot, cameraRig, camera);
        }
    }
}
