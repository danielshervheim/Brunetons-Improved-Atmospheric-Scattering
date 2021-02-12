// Dan Shervheim
// danielshervheim.com
// August 2019

#if UNITY_EDITOR

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

using BrunetonsImprovedAtmosphere;

public class AtmosphereEditorWindow : EditorWindow
{
    // Simulation parameters.
    public bool UseConstantSolarSpectrum = true;
    public bool UseOzone = true;
    public bool UseCombinedTextures = false;
    public bool UseHalfPrecision = true;
    public bool DoWhiteBalance = true;
    public LUMINANCE UseLuminance = LUMINANCE.PRECOMPUTED;
    public float Exposure = 10.0f;

    // Load from Resources.
    private ComputeShader _precomputation;
    private Material _material;

    // Private variables.
    private Model m_model;

    private Texture2D m_transmittanceTexture;
    private Texture3D m_scatteringTexture;
    private Texture3D m_singleMieScatteringTexture;
    private Texture2D m_irradianceTexture;

    private string _materialPath;
    private string _transmittanceTexturePath;
    private string _scatteringTexturePath;
    private string _singleMieScatteringTexturePath;
    private string _irradianceTexturePath;

    private bool _showErrorMessage = false;
    private string _errorMessage = "";

    // Private constants.
    private const float kSunAngularRadius = 0.00935f / 2.0f;
    private const float kBottomRadius = 6360000.0f;
    private const float kLengthUnitInMeters = 1000.0f;

    [MenuItem("Atmosphere/Generate")]
    static void Window()
    {
        AtmosphereEditorWindow window = CreateInstance(typeof(AtmosphereEditorWindow)) as AtmosphereEditorWindow;
        window.ShowUtility();
    }

    private void OnGUI()
    {
        // Verify that compute shaders are supported.
        if (!SystemInfo.supportsComputeShaders) {
            EditorGUILayout.HelpBox("This tool requires a GPU that supports compute shaders.", MessageType.Error);
            if (GUILayout.Button("Close")) {
                Close();
            }
            return;
        }
        else {
            const float SPACE = 12.0f;

            GUILayout.Label("Atmosphere Generator");

            GUILayout.Space(SPACE);
            GUILayout.Label("Precomputation Parameters");
            UseConstantSolarSpectrum = GUILayout.Toggle(UseConstantSolarSpectrum, "Use constant solar spectrum");
            UseOzone = GUILayout.Toggle(UseOzone, "Use ozone");
            UseCombinedTextures = GUILayout.Toggle(UseCombinedTextures, "Use combined textures");
            UseHalfPrecision = GUILayout.Toggle(UseHalfPrecision, "Use half precision");
            DoWhiteBalance = GUILayout.Toggle(DoWhiteBalance, "Do white balance");
            UseLuminance = (LUMINANCE)EditorGUILayout.EnumPopup("Luminance", UseLuminance);
            Exposure = EditorGUILayout.FloatField("Exposure", Exposure);

            GUILayout.Space(SPACE);
            if (GUILayout.Button("Precompute")) {
                try {
                    LoadResources();
                    SetTexturePaths();
                    Verify();
                    _showErrorMessage = false;
                    Precompute();
                    SaveTextures();
                    UpdateMaterial();
                }
                catch (Exception e) {
                    _showErrorMessage = true;
                    _errorMessage = e.Message;
                }
            }

            if (_showErrorMessage) {
                EditorGUILayout.HelpBox(_errorMessage, MessageType.Error);
            }
        }
    }

    private void LoadResources()
    {
        _precomputation = Resources.Load("Precomputation") as ComputeShader;
        if (_precomputation == null) {
            throw new ArgumentException("Unable to load Precomputation.compute from Resources.");
        }
        Shader s = Resources.Load("Atmosphere") as Shader;
        if (s == null) {
            throw new ArgumentException("Unable to load Atmosphere.shader from Resources.");
        }
        _material = new Material(s);
    }

    private void SetTexturePaths()
    {
        // Prompt the user to save the file.
        _materialPath = EditorUtility.SaveFilePanelInProject("Save As", "AtmosphericSkybox", "mat", "");
        _transmittanceTexturePath = EditorUtility.SaveFilePanelInProject("Save As", "transmittance", "asset", "");
        _scatteringTexturePath = EditorUtility.SaveFilePanelInProject("Save As", "scattering", "asset", "");
        _singleMieScatteringTexturePath = EditorUtility.SaveFilePanelInProject("Save As", "singleMieScattering", "asset", "");
        _irradianceTexturePath = EditorUtility.SaveFilePanelInProject("Save As", "irradiance", "asset", "");
    }

    private void Verify()
    {
        if (_precomputation == null) {
            throw new ArgumentException("The precomputation compute shader must be assigned.");
        }

        if (_material == null) {
            throw new ArgumentException("The material must be assigned.");
        }

        if (_material.shader != Shader.Find("Skybox/Atmosphere")) {
            throw new ArgumentException("The material must use the shader: \"Skybox/Atmosphere\".");
        }

        if (_materialPath == null || _materialPath.Equals("")) {
            throw new ArgumentException("Invalid path provided for the material.");
        }
        if (_transmittanceTexturePath == null || _transmittanceTexturePath.Equals("")) {
            throw new ArgumentException("Invalid path provided for the transmittance texture.");
        }
        if (_scatteringTexturePath == null || _scatteringTexturePath.Equals("")) {
            throw new ArgumentException("Invalid path provided for the scattering texture.");
        }
        if (_singleMieScatteringTexturePath == null || _singleMieScatteringTexturePath.Equals("")) {
            throw new ArgumentException("Invalid path provided for the single Mie scattering texture.");
        }
        if (_irradianceTexturePath == null || _irradianceTexturePath.Equals("")) {
            throw new ArgumentException("Invalid path provided for the irradiance texture.");
        }
    }

    private void Precompute()
    {
        // Values from "Reference Solar Spectral Irradiance: ASTM G-173", ETR column
        // (see http://rredc.nrel.gov/solar/spectra/am1.5/ASTMG173/ASTMG173.html),
        // summed and averaged in each bin (e.g. the value for 360nm is the average
        // of the ASTM G-173 values for all wavelengths between 360 and 370nm).
        // Values in W.m^-2.
        int kLambdaMin = 360;
        int kLambdaMax = 830;

        double[] kSolarIrradiance = new double[]
        {
            1.11776, 1.14259, 1.01249, 1.14716, 1.72765, 1.73054, 1.6887, 1.61253,
            1.91198, 2.03474, 2.02042, 2.02212, 1.93377, 1.95809, 1.91686, 1.8298,
            1.8685, 1.8931, 1.85149, 1.8504, 1.8341, 1.8345, 1.8147, 1.78158, 1.7533,
            1.6965, 1.68194, 1.64654, 1.6048, 1.52143, 1.55622, 1.5113, 1.474, 1.4482,
            1.41018, 1.36775, 1.34188, 1.31429, 1.28303, 1.26758, 1.2367, 1.2082,
            1.18737, 1.14683, 1.12362, 1.1058, 1.07124, 1.04992
        };

        // Values from http://www.iup.uni-bremen.de/gruppen/molspec/databases/
        // referencespectra/o3spectra2011/index.html for 233K, summed and averaged in
        // each bin (e.g. the value for 360nm is the average of the original values
        // for all wavelengths between 360 and 370nm). Values in m^2.
        double[] kOzoneCrossSection = new double[]
        {
            1.18e-27, 2.182e-28, 2.818e-28, 6.636e-28, 1.527e-27, 2.763e-27, 5.52e-27,
            8.451e-27, 1.582e-26, 2.316e-26, 3.669e-26, 4.924e-26, 7.752e-26, 9.016e-26,
            1.48e-25, 1.602e-25, 2.139e-25, 2.755e-25, 3.091e-25, 3.5e-25, 4.266e-25,
            4.672e-25, 4.398e-25, 4.701e-25, 5.019e-25, 4.305e-25, 3.74e-25, 3.215e-25,
            2.662e-25, 2.238e-25, 1.852e-25, 1.473e-25, 1.209e-25, 9.423e-26, 7.455e-26,
            6.566e-26, 5.105e-26, 4.15e-26, 4.228e-26, 3.237e-26, 2.451e-26, 2.801e-26,
            2.534e-26, 1.624e-26, 1.465e-26, 2.078e-26, 1.383e-26, 7.105e-27
        };

        // From https://en.wikipedia.org/wiki/Dobson_unit, in molecules.m^-2.
        double kDobsonUnit = 2.687e20;
        // Maximum number density of ozone molecules, in m^-3 (computed so at to get
        // 300 Dobson units of ozone - for this we divide 300 DU by the integral of
        // the ozone density profile defined below, which is equal to 15km).
        double kMaxOzoneNumberDensity = 300.0 * kDobsonUnit / 15000.0;
        // Wavelength independent solar irradiance "spectrum" (not physically
        // realistic, but was used in the original implementation).
        double kConstantSolarIrradiance = 1.5;
        double kTopRadius = 6420000.0;
        double kRayleigh = 1.24062e-6;
        double kRayleighScaleHeight = 8000.0;
        double kMieScaleHeight = 1200.0;
        double kMieAngstromAlpha = 0.0;
        double kMieAngstromBeta = 5.328e-3;
        double kMieSingleScatteringAlbedo = 0.9;
        double kMiePhaseFunctionG = 0.8;
        double kGroundAlbedo = 0.1;
        double max_sun_zenith_angle = (UseHalfPrecision ? 102.0 : 120.0) / 180.0 * Mathf.PI;

        DensityProfileLayer rayleigh_layer = new DensityProfileLayer("rayleigh", 0.0, 1.0, -1.0 / kRayleighScaleHeight, 0.0, 0.0);
        DensityProfileLayer mie_layer = new DensityProfileLayer("mie", 0.0, 1.0, -1.0 / kMieScaleHeight, 0.0, 0.0);

        // Density profile increasing linearly from 0 to 1 between 10 and 25km, and
        // decreasing linearly from 1 to 0 between 25 and 40km. This is an approximate
        // profile from http://www.kln.ac.lk/science/Chemistry/Teaching_Resources/
        // Documents/Introduction%20to%20atmospheric%20chemistry.pdf (page 10).
        List<DensityProfileLayer> ozone_density = new List<DensityProfileLayer>();
        ozone_density.Add(new DensityProfileLayer("absorption0", 25000.0, 0.0, 0.0, 1.0 / 15000.0, -2.0 / 3.0));
        ozone_density.Add(new DensityProfileLayer("absorption1", 0.0, 0.0, 0.0, -1.0 / 15000.0, 8.0 / 3.0));

        List<double> wavelengths = new List<double>();
        List<double> solar_irradiance = new List<double>();
        List<double> rayleigh_scattering = new List<double>();
        List<double> mie_scattering = new List<double>();
        List<double> mie_extinction = new List<double>();
        List<double> absorption_extinction = new List<double>();
        List<double> ground_albedo = new List<double>();

        for (int l = kLambdaMin; l <= kLambdaMax; l += 10)
        {
            double lambda = l * 1e-3;  // micro-meters
            double mie = kMieAngstromBeta / kMieScaleHeight * Math.Pow(lambda, -kMieAngstromAlpha);

            wavelengths.Add(l);

            if (UseConstantSolarSpectrum)
                solar_irradiance.Add(kConstantSolarIrradiance);
            else
                solar_irradiance.Add(kSolarIrradiance[(l - kLambdaMin) / 10]);

            rayleigh_scattering.Add(kRayleigh * Math.Pow(lambda, -4));
            mie_scattering.Add(mie * kMieSingleScatteringAlbedo);
            mie_extinction.Add(mie);
            absorption_extinction.Add(UseOzone ? kMaxOzoneNumberDensity * kOzoneCrossSection[(l - kLambdaMin) / 10] : 0.0);
            ground_albedo.Add(kGroundAlbedo);
        }

        m_model = new Model();

        m_model.HalfPrecision = UseHalfPrecision;
        m_model.CombineScatteringTextures = UseCombinedTextures;
        m_model.UseLuminance = UseLuminance;
        m_model.Wavelengths = wavelengths;
        m_model.SolarIrradiance = solar_irradiance;
        m_model.SunAngularRadius = kSunAngularRadius;
        m_model.BottomRadius = kBottomRadius;
        m_model.TopRadius = kTopRadius;
        m_model.RayleighDensity = rayleigh_layer;
        m_model.RayleighScattering = rayleigh_scattering;
        m_model.MieDensity = mie_layer;
        m_model.MieScattering = mie_scattering;
        m_model.MieExtinction = mie_extinction;
        m_model.MiePhaseFunctionG = kMiePhaseFunctionG;
        m_model.AbsorptionDensity = ozone_density;
        m_model.AbsorptionExtinction = absorption_extinction;
        m_model.GroundAlbedo = ground_albedo;
        m_model.MaxSunZenithAngle = max_sun_zenith_angle;
        m_model.LengthUnitInMeters = kLengthUnitInMeters;

        int numScatteringOrders = 4;
        m_model.Init(_precomputation, numScatteringOrders);

        m_model.BindToMaterial(_material);

        // Get the textures from the model and save them to this instance.
        RenderTexture transmittance, scattering, singleMieScattering, irradiance;
        m_model.GetTextures(out transmittance, out scattering, out singleMieScattering, out irradiance);
        m_transmittanceTexture = transmittance.ToTexture2D();
        m_scatteringTexture = scattering.ToTexture3D();
        m_singleMieScatteringTexture = singleMieScattering.ToTexture3D();
        m_irradianceTexture = irradiance.ToTexture2D();

        // Release the temporary render textures.
        m_model.Release();
    }

    private void SaveTextures()
    {
        AssetDatabase.CreateAsset(_material, _materialPath);
        AssetDatabase.CreateAsset(m_transmittanceTexture, _transmittanceTexturePath);
        AssetDatabase.CreateAsset(m_scatteringTexture, _scatteringTexturePath);
        AssetDatabase.CreateAsset(m_singleMieScatteringTexture, _singleMieScatteringTexturePath);
        AssetDatabase.CreateAsset(m_irradianceTexture, _irradianceTexturePath);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
    }

    private void UpdateMaterial()
    {
        _material.SetFloat("exposure", UseLuminance != LUMINANCE.NONE ? Exposure * 1e-5f : Exposure);
        _material.SetVector("earth_center", new Vector3(0.0f, -kBottomRadius / kLengthUnitInMeters, 0.0f));
        _material.SetVector("sun_size", new Vector2(Mathf.Tan(kSunAngularRadius), Mathf.Cos(kSunAngularRadius)));

        _material.SetTexture("transmittance_texture", m_transmittanceTexture);
        _material.SetTexture("scattering_texture", m_scatteringTexture);
        _material.SetTexture("single_mie_scattering_texture", m_singleMieScatteringTexture);
        _material.SetTexture("irradiance_texture", m_irradianceTexture);

        double white_point_r = 1.0;
        double white_point_g = 1.0;
        double white_point_b = 1.0;

        if (DoWhiteBalance)
        {
            if (m_model != null)
            {
                m_model.ConvertSpectrumToLinearSrgb(out white_point_r, out white_point_g, out white_point_b);
            }

            double white_point = (white_point_r + white_point_g + white_point_b) / 3.0;
            white_point_r /= white_point;
            white_point_g /= white_point;
            white_point_b /= white_point;
        }

        _material.SetVector("white_point", new Vector3((float)white_point_r, (float)white_point_g, (float)white_point_b));

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
    }

    /*
    public void MakeAtmosphere()
    {
        // Material.
        if (m_skyboxMaterial != null && AssetDatabase.Contains(m_skyboxMaterial))
        {
            AssetDatabase.RemoveObjectFromAsset(m_skyboxMaterial);
        }
        m_skyboxMaterial = new Material(Shader.Find("Skybox/Atmosphere"));
        m_skyboxMaterial.name = "skyboxMaterial";
        AssetDatabase.AddObjectToAsset(m_skyboxMaterial, this);
        AssetDatabase.ImportAsset(AssetDatabase.GetAssetPath(m_skyboxMaterial));
        SerializeAsset(m_skyboxMaterial);

        Precompute();

        // Transmittance texture.
        if (m_transmittanceTexture != null && AssetDatabase.Contains(m_transmittanceTexture))
        {
            AssetDatabase.RemoveObjectFromAsset(m_transmittanceTexture);
        }
        m_transmittanceTexture.name = "transmittanceTexture";
        AssetDatabase.AddObjectToAsset(m_transmittanceTexture, this);
        AssetDatabase.ImportAsset(AssetDatabase.GetAssetPath(m_transmittanceTexture));
        SerializeAsset(m_transmittanceTexture);

        // Scattering texture.
        if (m_scatteringTexture != null && AssetDatabase.Contains(m_scatteringTexture))
        {
            AssetDatabase.RemoveObjectFromAsset(m_scatteringTexture);
        }
        m_scatteringTexture.name = "scatteringTexture";
        AssetDatabase.AddObjectToAsset(m_scatteringTexture, this);
        AssetDatabase.ImportAsset(AssetDatabase.GetAssetPath(m_scatteringTexture));
        SerializeAsset(m_scatteringTexture);

        // Mie scattering texture.
        if (m_singleMieScatteringTexture != null && AssetDatabase.Contains(m_singleMieScatteringTexture))
        {
            AssetDatabase.RemoveObjectFromAsset(m_singleMieScatteringTexture);
        }
        m_singleMieScatteringTexture.name = "singleMieScatteringTexture";
        AssetDatabase.AddObjectToAsset(m_singleMieScatteringTexture, this);
        AssetDatabase.ImportAsset(AssetDatabase.GetAssetPath(m_singleMieScatteringTexture));
        SerializeAsset(m_singleMieScatteringTexture);

        // Irradiance texture.
        if (m_irradianceTexture != null && AssetDatabase.Contains(m_irradianceTexture))
        {
            AssetDatabase.RemoveObjectFromAsset(m_irradianceTexture);
        }
        m_irradianceTexture.name = "irradianceTexture";
        AssetDatabase.AddObjectToAsset(m_irradianceTexture, this);
        AssetDatabase.ImportAsset(AssetDatabase.GetAssetPath(m_irradianceTexture));
        SerializeAsset(m_irradianceTexture);

        // Set material textures.
        m_skyboxMaterial.SetTexture("transmittance_texture", m_transmittanceTexture);
        m_skyboxMaterial.SetTexture("scattering_texture", m_scatteringTexture);
        m_skyboxMaterial.SetTexture("single_mie_scattering_texture", m_singleMieScatteringTexture);
        m_skyboxMaterial.SetTexture("irradiance_texture", m_irradianceTexture);
    }

    private void SerializeAsset(UnityEngine.Object asset)
    {
        AssetDatabase.Refresh();
        EditorUtility.SetDirty(asset);
        AssetDatabase.SaveAssets();
    }





    private void OnValidate()
    {
        if (m_skyboxMaterial != null)
        {
            UpdateMaterial();
        }
    }
    */
}  // Atmosphere

#endif
