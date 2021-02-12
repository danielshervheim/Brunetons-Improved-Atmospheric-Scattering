// Dan Shervheim
// danielshervheim.com
// August 2019

Shader "Skybox/Atmosphere"
{
    Properties
    {
		units_to_atmosphere_boundary ("Units to Atmosphere Boundary", float) = 6000
        [Toggle] tonemap("Tonemap", Int) = 1
        [Toggle] gammaCorrection("Gamma Correction", Int) = 0
        [Toggle] clampViewVector("Clamp View At Horizon", Int) = 0

		// Textures.
		[HideInInspector] transmittance_texture("Transmittance", 2D) = "white" {}
		[HideInInspector] scattering_texture("Scattering", 3D) = "white" {}
		[HideInInspector] single_mie_scattering_texture("Single Mie Scattering", 3D) = "white" {}
		[HideInInspector] irradiance_texture("Irradiance", 2D) = "white" {}

		// Parameters.
		[HideInInspector] exposure("exposure", Float) = 0.0001
		[HideInInspector] white_point("white_point", Vector) = (1.180388, 0.9290531, 0.8905591)
		[HideInInspector] earth_center("earth_center", Vector) = (0,0,0)  // todo: hide
		[HideInInspector] sun_size("sun_size", Vector) = (0.004675034, 0.9999891, 0)

		// Advanced parameters.
		[HideInInspector] sun_angular_radius("sun_angular_radius", Float) = 0.004675034
		[HideInInspector] bottom_radius("bottom_radius", Float) = 6360
		[HideInInspector] top_radius("top_radius", Float) = 6420
		[HideInInspector] mie_phase_function_g("mie_phase_function_g", Range(-1, 1)) = 0.8
		[HideInInspector] mu_s_min("mu_s_min", Float) = -0.207911
		[HideInInspector] SKY_SPECTRAL_RADIANCE_TO_LUMINANCE("SKY_SPECTRAL_RADIANCE_TO_LUMINANCE", Vector) = (0, 0, 0)
		[HideInInspector] SUN_SPECTRAL_RADIANCE_TO_LUMINANCE("SUN_SPECTRAL_RADIANCE_TO_LUMINANCE", Vector) = (0, 0, 0)
		[HideInInspector] solar_irradiance("solar_irradiance", Vector) = (1.5, 1.5, 1.5)
		[HideInInspector] rayleigh_scattering("rayleigh_scattering", Vector) = (0.005802339, 0.01355776, 0.03310001)
		[HideInInspector] mie_scattering("mie_scattering", Vector) = (0.003996, 0.003996, 0.003996)

		// Texture properties.
		[HideInInspector] TRANSMITTANCE_TEXTURE_WIDTH("TRANSMITTANCE_TEXTURE_WIDTH", Int) = 256
		[HideInInspector] TRANSMITTANCE_TEXTURE_HEIGHT("TRANSMITTANCE_TEXTURE_HEIGHT", Int) = 64
		[HideInInspector] SCATTERING_TEXTURE_R_SIZE("SCATTERING_TEXTURE_R_SIZE", Int) = 32
		[HideInInspector] SCATTERING_TEXTURE_MU_SIZE("SCATTERING_TEXTURE_MU_SIZE", Int) = 128
		[HideInInspector] SCATTERING_TEXTURE_MU_S_SIZE("SCATTERING_TEXTURE_MU_S_SIZE", Int) = 32
		[HideInInspector] SCATTERING_TEXTURE_NU_SIZE("SCATTERING_TEXTURE_NU_SIZE", Int) = 8
		[HideInInspector] SCATTERING_TEXTURE_WIDTH("SCATTERING_TEXTURE_WIDTH", Int) = 256
		[HideInInspector] SCATTERING_TEXTURE_HEIGHT("SCATTERING_TEXTURE_HEIGHT", Int) = 128
		[HideInInspector] SCATTERING_TEXTURE_DEPTH("SCATTERING_TEXTURE_DEPTH", Int) = 32
		[HideInInspector] IRRADIANCE_TEXTURE_WIDTH("IRRADIANCE_TEXTURE_WIDTH", Int) = 64
		[HideInInspector] IRRADIANCE_TEXTURE_HEIGHT("IRRADIANCE_TEXTURE_HEIGHT", Int) = 16
    }
    SubShader
    {
		Tags
		{
			"Queue" = "Background"
			"RenderType" = "Background"
			"PreviewType" = "Skybox"
			"IsEmissive" = "true"
		}

		Cull Off ZWrite Off

		Pass
		{
			CGPROGRAM
			#pragma vertex vert
			#pragma fragment frag

			#pragma multi_compile __ RADIANCE_API_ENABLED
			#pragma multi_compile __ COMBINED_SCATTERING_TEXTURES

			#include "UnityCG.cginc"
			#include "Definitions.cginc"
			#include "UtilityFunctions.cginc"
			#include "TransmittanceFunctions.cginc"
			#include "ScatteringFunctions.cginc"
			#include "IrradianceFunctions.cginc"
			#include "RenderingFunctions.cginc"

            float units_to_atmosphere_boundary;
            uint tonemap;
            uint gammaCorrection;
            uint clampViewVector;

			// Textures.
			sampler2D transmittance_texture;
			sampler3D scattering_texture;
			sampler3D single_mie_scattering_texture;
			sampler2D irradiance_texture;

			// Parameters.
			float exposure;
			float3 white_point;
			float3 earth_center;
			float2 sun_size;

			// Advanced parameters and texture properties are in Definitions.cginc.

			struct appdata
			{
				float4 vertex : POSITION;
				float3 uvw : TEXCOORD0;
			};

			struct v2f
			{
				float4 vertex : SV_POSITION;
                float3 view_ray : TEXCOORD0;
			};

			v2f vert(appdata v)
			{
				v2f o;
				o.vertex = UnityObjectToClipPos(v.vertex);
				o.view_ray = v.uvw;
				return o;
			}

			#ifdef RADIANCE_API_ENABLED
				RadianceSpectrum GetSolarRadiance()
				{
					return solar_irradiance / (PI * sun_angular_radius * sun_angular_radius);
				}

				RadianceSpectrum GetSkyRadiance(Position camera, Direction view_ray, Length shadow_length,
				Direction sun_direction, out DimensionlessSpectrum transmittance)
				{
					return GetSkyRadiance(transmittance_texture,
						scattering_texture, single_mie_scattering_texture,
						camera, view_ray, shadow_length, sun_direction, transmittance);
				}
			#else
				Luminance3 GetSolarRadiance()
				{
					return solar_irradiance / (PI * sun_angular_radius * sun_angular_radius) *
						SUN_SPECTRAL_RADIANCE_TO_LUMINANCE;
				}

				Luminance3 GetSkyRadiance(Position camera, Direction view_ray, Length shadow_length,
				Direction sun_direction, out DimensionlessSpectrum transmittance)
				{
					return GetSkyRadiance(
						transmittance_texture,
						scattering_texture, single_mie_scattering_texture,
						camera, view_ray, shadow_length, sun_direction, transmittance) *
						SKY_SPECTRAL_RADIANCE_TO_LUMINANCE;
				}
			#endif

			fixed4 frag(v2f i) : SV_Target
			{
				float3 view_position = _WorldSpaceCameraPos;
				float3 view_direction = normalize(i.view_ray);
                if (clampViewVector == 1)
                {
                    view_direction.y = saturate(view_direction.y);
                }
				float3 sun_direction = _WorldSpaceLightPos0.xyz;

				float shadow_length = 0;
				float3 transmittance;

                float3 p = view_position;
                p /= units_to_atmosphere_boundary;
                p *= (top_radius - bottom_radius);
                p.y += bottom_radius;

				float3 radiance = GetSkyRadiance(p, view_direction, shadow_length, sun_direction, transmittance);

				// If the view ray intersects the Sun, add the Sun radiance.
				if (dot(view_direction, sun_direction) > sun_size.y)
				{
					radiance += transmittance * GetSolarRadiance() * 1e-4;
				}

                radiance = (radiance/white_point)*exposure;

                if (tonemap == 1) {
                    radiance = 1-exp(-radiance);
                }

                if (gammaCorrection == 1) {
                    radiance = pow(radiance, 1.0/2.2);
                }

				return float4(radiance, 1);
			}
            ENDCG
        }
    }
}
