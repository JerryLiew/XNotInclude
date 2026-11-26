using UnityEngine;

public enum SimOverlayMode
{
    Temperature,
    Material,
    Mass
}

public sealed class SimVisualizer : MonoBehaviour
{
    [SerializeField] private Shader heatmapShader;
    [SerializeField] private float minTempC = -20f;
    [SerializeField] private float maxTempC = 80f;
    [SerializeField] private SimOverlayMode overlayMode = SimOverlayMode.Temperature;

    private SimGridData _grid;
    private Texture2D _texture;
    private float[] _scalars;
    private Material _material;
    private Mesh _mesh;
    private MeshRenderer _renderer;

    public SimOverlayMode Mode
    {
        get => overlayMode;
        set => overlayMode = value;
    }

    public float MinTempC
    {
        get => minTempC;
        set => minTempC = value;
    }

    public float MaxTempC
    {
        get => maxTempC;
        set => maxTempC = value;
    }

    public void Initialize(SimGridData grid)
    {
        _grid = grid;
        _scalars = new float[grid.CellCount];

        _texture = new Texture2D(grid.Width, grid.Height, TextureFormat.RFloat, false, true)
        {
            name = "SimScalarField",
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp
        };
        _texture.SetPixelData(_scalars, 0);
        _texture.Apply(false, false);

        Shader shader = heatmapShader != null ? heatmapShader : Shader.Find("XNotInclude/ThermalHeatmap");
        if (shader == null)
        {
            Debug.LogError("ThermalHeatmap shader not found. Check Presentation/Shaders/ThermalHeatmap.shader.");
            return;
        }

        _material = new Material(shader) { name = "SimHeatmapMaterial" };
        _material.SetTexture("_ScalarTex", _texture);

        _mesh = BuildQuad(grid.Width, grid.Height);

        var view = new GameObject("SimGridView");
        view.transform.SetParent(transform, false);
        view.AddComponent<MeshFilter>().sharedMesh = _mesh;

        _renderer = view.AddComponent<MeshRenderer>();
        _renderer.sharedMaterial = _material;
        _renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        _renderer.receiveShadows = false;

        Refresh();
    }

    public void Refresh()
    {
        if (_grid == null || _texture == null || _scalars == null)
        {
            return;
        }

        switch (overlayMode)
        {
            case SimOverlayMode.Temperature:
            {
                float invRange = 1f / Mathf.Max(0.001f, maxTempC - minTempC);
                for (int i = 0; i < _scalars.Length; i++)
                {
                    _scalars[i] = (_grid.Temperatures[i] - minTempC) * invRange;
                }
                break;
            }
            case SimOverlayMode.Material:
            {
                for (int i = 0; i < _scalars.Length; i++)
                {
                    _scalars[i] = _grid.ElementIds[i] / 16f;
                }
                break;
            }
            case SimOverlayMode.Mass:
            {
                for (int i = 0; i < _scalars.Length; i++)
                {
                    ushort id = _grid.ElementIds[i];
                    ElementDef def = id < _grid.ElementDefs.Length ? _grid.ElementDefs[id] : default;
                    float maxMass = def.MaxMass > 0f ? def.MaxMass : 1f;
                    _scalars[i] = Mathf.Clamp01(_grid.Masses[i] / maxMass);
                }
                break;
            }
        }

        _texture.SetPixelData(_scalars, 0);
        _texture.Apply(false, false);

        if (_material != null)
        {
            _material.SetFloat("_Mode", overlayMode == SimOverlayMode.Material ? 1f : 0f);
        }
    }

    public void Dispose()
    {
        if (_renderer != null)
        {
            Destroy(_renderer.gameObject);
        }

        if (_mesh != null)
        {
            Destroy(_mesh);
        }

        if (_material != null)
        {
            Destroy(_material);
        }

        if (_texture != null)
        {
            Destroy(_texture);
        }

        _grid = null;
        _scalars = null;
    }

    private static Mesh BuildQuad(int width, int height)
    {
        float halfWidth = width * 0.5f;
        float halfHeight = height * 0.5f;

        var mesh = new Mesh { name = "SimGridQuad" };
        mesh.vertices = new[]
        {
            new Vector3(-halfWidth, -halfHeight, 0f),
            new Vector3(halfWidth, -halfHeight, 0f),
            new Vector3(halfWidth, halfHeight, 0f),
            new Vector3(-halfWidth, halfHeight, 0f)
        };
        mesh.uv = new[]
        {
            new Vector2(0f, 0f),
            new Vector2(1f, 0f),
            new Vector2(1f, 1f),
            new Vector2(0f, 1f)
        };
        mesh.triangles = new[] { 0, 2, 1, 0, 3, 2 };
        mesh.RecalculateBounds();
        return mesh;
    }
}
