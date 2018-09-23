#pragma warning disable 0618

using UnityEngine;
using System.Collections;
using System;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Formatters.Binary;
using UnityEngine.Rendering;
using System.Collections.Generic;
using Unity.Jobs;
using Unity.Collections;

[ExecuteInEditMode]
[RequireComponent(typeof(Camera))]
[AddComponentMenu("Image Effects/Sonic Ether/SEGICascaded")]
public class SEGICascaded : MonoBehaviour
{
	object initChecker;

	Material material;
	Camera attachedCamera;
	Transform shadowCamTransform;

	Camera shadowCam;
	GameObject shadowCamGameObject;
    Texture2D[] blueNoise;

    public ReflectionProbe reflectionProbe;
    GameObject reflectionProbeGameObject;

    [Serializable]
    public enum VoxelResolution
    {
        low = 64,
        medium = 128,
        high = 256
    }

    public VoxelResolution voxelResolution = VoxelResolution.high;

    public bool visualizeSunDepthTexture = false;
	public bool visualizeGI = false;

	public Light sun;
	public LayerMask giCullingMask = 2147483647;

	public float shadowSpaceSize = 50.0f;

	[Range(0.01f, 1.0f)]
	public float temporalBlendWeight = 0.1f;

	public bool visualizeVoxels = false;

	public bool updateGI = true;


	public Color skyColor;
    public bool MatchAmbiantColor;

    public float voxelSpaceSize = 50.0f;

	public bool useBilateralFiltering = false;

	[Range(0, 2)]
	public int innerOcclusionLayers = 1;


    [Range(1, 16)]
    public int GIResolution = 1;
    public bool stochasticSampling = true;
	public bool infiniteBounces = false;
	public Transform followTransform;
	[Range(1, 128)]
	public int cones = 6;
	[Range(1, 32)]
	public int coneTraceSteps = 14;
	[Range(0.1f, 2.0f)]
	public float coneLength = 1.0f;
	[Range(0.5f, 6.0f)]
	public float coneWidth = 5.5f;
	[Range(0.0f, 4.0f)]
	public float occlusionStrength = 1.0f;
	[Range(0.0f, 4.0f)]
	public float nearOcclusionStrength = 0.5f;
	[Range(0.001f, 4.0f)]
	public float occlusionPower = 1.5f;
	[Range(0.0f, 4.0f)]
	public float coneTraceBias = 1.0f;
	[Range(0.0f, 4.0f)]
	public float nearLightGain = 1.0f;
	[Range(0.0f, 4.0f)]
	public float giGain = 1.0f;
	[Range(0.0f, 4.0f)]
	public float secondaryBounceGain = 1.0f;
	[Range(0.0f, 16.0f)]
	public float softSunlight = 0.0f;

	[Range(0.0f, 8.0f)]
	public float skyIntensity = 1.0f;

	public bool doReflections = true;
	[Range(12, 128)]
	public int reflectionSteps = 64;
	[Range(0.001f, 4.0f)]
	public float reflectionOcclusionPower = 1.0f;
	[Range(0.0f, 1.0f)]
	public float skyReflectionIntensity = 1.0f;



	[Range(0.1f, 4.0f)]
	public float farOcclusionStrength = 1.0f;
	[Range(0.1f, 4.0f)]
	public float farthestOcclusionStrength = 1.0f;

	[Range(3, 16)]
	public int secondaryCones = 6;
	[Range(0.1f, 4.0f)]
	public float secondaryOcclusionStrength = 1.0f;

	public bool sphericalSkylight = false;


    struct Pass
    {
        public static int DiffuseTrace = 0;
        public static int BilateralBlur = 1;
        public static int BlendWithScene = 2;
        public static int TemporalBlend = 3;
        public static int SpecularTrace = 4;
        public static int GetCameraDepthTexture = 5;
        public static int GetWorldNormals = 6;
        public static int VisualizeGI = 7;
        public static int WriteBlack = 8;
        public static int VisualizeVoxels = 10;
        public static int BilateralUpsample = 11;
    }

    struct SEGICMDBufferRT
    {
        // 0    - FXAART
        // 1    - gi1
        // 2    - gi2
        // 3    - reflections
        // 4    - gi3
        // 5    - gi4
        // 6    - blur0
        // 7    - blur1
        // 8    - FXAARTluminance
        public static int FXAART = 0;
        public static int gi1 = 1;
        public static int gi2 = 2;
        public static int reflections = 3;
        public static int gi3 = 4;
        public static int gi4 = 5;
        public static int blur0 = 6;
        public static int blur1 = 7;
        public static int FXAARTluminance = 8;
    }
    private RenderTexture RT_FXAART;
    private RenderTexture RT_gi1;
    private RenderTexture RT_gi2;
    private RenderTexture RT_reflections;
    private RenderTexture RT_gi3;
    private RenderTexture RT_gi4;
    private RenderTexture RT_blur0;
    private RenderTexture RT_blur1;
    private RenderTexture RT_FXAARTluminance;

    public RenderTexture SEGIRenderSource;
    public RenderTexture SEGIRenderDestination;
    public int SEGIRenderWidth;
    public int SEGIRenderHeight;


	public struct SystemSupported
	{
		public bool hdrTextures;
		public bool rIntTextures;
		public bool dx11;
		public bool volumeTextures;
		public bool postShader;
		public bool sunDepthShader;
		public bool voxelizationShader;
		public bool tracingShader;

        public bool fullFunctionality
		{
			get
			{
				return hdrTextures && rIntTextures && dx11 && volumeTextures && postShader && sunDepthShader && voxelizationShader && tracingShader;
			}
		}
	}

	/// <summary>
	/// Contains info on system compatibility of required hardware functionality
	/// </summary>
	public SystemSupported systemSupported;

	/// <summary>
	/// Estimates the VRAM usage of all the render textures used to render GI.
	/// </summary>
	public float vramUsage
	{
		get
		{
			long v = 0;

			if (sunDepthTexture != null)
				v += sunDepthTexture.width * sunDepthTexture.height * 16;

			if (previousResult != null)
				v += previousResult.width * previousResult.height * 16 * 4;

			if (previousDepth != null)
				v += previousDepth.width * previousDepth.height * 32;

			if (intTex1 != null)
				v += intTex1.width * intTex1.height * intTex1.volumeDepth * 32;

			if (volumeTextures != null)
			{
				for (int i = 0; i < volumeTextures.Length; i++)
				{
					if (volumeTextures[i] != null)
						v += volumeTextures[i].width * volumeTextures[i].height * volumeTextures[i].volumeDepth * 16 * 4;
				}
			}

			if (volumeTexture1 != null)
				v += volumeTexture1.width * volumeTexture1.height * volumeTexture1.volumeDepth * 16 * 4;

			if (volumeTextureB != null)
				v += volumeTextureB.width * volumeTextureB.height * volumeTextureB.volumeDepth * 16 * 4;

			if (dummyVoxelTexture != null)
				v += dummyVoxelTexture.width * dummyVoxelTexture.height * 8;

			if (dummyVoxelTexture2 != null)
				v += dummyVoxelTexture2.width * dummyVoxelTexture2.height * 8;

			float vram = (v / 8388608.0f);

			return vram;
		}
	}

    public FilterMode filterMode = FilterMode.Point;
    public RenderTextureFormat renderTextureFormat = RenderTextureFormat.ARGBHalf;



	public bool gaussianMipFilter = false;

	int mipFilterKernel
	{
		get
		{
			return gaussianMipFilter ? 1 : 0;
		}
	}

	public bool voxelAA = false;

    int dummyVoxelResolution
    {
        get
        {
            return (int)voxelResolution * (voxelAA ? 2 : 1);
        }
    }

    int sunShadowResolution = 256;
	int prevSunShadowResolution;





	Shader sunDepthShader;

	float shadowSpaceDepthRatio = 10.0f;

	int frameSwitch = 0;

	RenderTexture sunDepthTexture;
	RenderTexture previousResult;
	RenderTexture previousDepth;
	RenderTexture intTex1;
	RenderTexture[] volumeTextures;
	RenderTexture volumeTexture1;
	RenderTexture volumeTextureB;

	RenderTexture activeVolume;
	RenderTexture previousActiveVolume;

	RenderTexture dummyVoxelTexture;
	RenderTexture dummyVoxelTexture2;

	bool notReadyToRender = false;

	Shader voxelizationShader;
	Shader voxelTracingShader;

	ComputeShader clearCompute;
	ComputeShader transferInts;
	ComputeShader mipFilter;

	const int numMipLevels = 6;

	Camera voxelCamera;
	GameObject voxelCameraGO;
	GameObject leftViewPoint;
	GameObject topViewPoint;

	float voxelScaleFactor
	{
		get
		{
			return (float)voxelResolution / 256.0f;
		}
	}

	Vector3 voxelSpaceOrigin;
	Vector3 previousVoxelSpaceOrigin;
	Vector3 voxelSpaceOriginDelta;


	Quaternion rotationFront = new Quaternion(0.0f, 0.0f, 0.0f, 1.0f);
	Quaternion rotationLeft = new Quaternion(0.0f, 0.7f, 0.0f, 0.7f);
	Quaternion rotationTop = new Quaternion(0.7f, 0.0f, 0.0f, 0.7f);

	int voxelFlipFlop = 0;


    int giRenderRes
    {
        get
        {
            return GIResolution;
        }
    }

    enum RenderState
	{
		Voxelize,
		Bounce
	}

	RenderState renderState = RenderState.Voxelize;

    //CommandBuffer refactor
    public CommandBuffer SEGIBuffer;

    //Gaussian Filter
    private Shader Gaussian_Shader;
    private Material Gaussian_Material;

    //FXAA
    public bool useFXAA;
    private Shader FXAA_Shader;
    private Material FXAA_Material;

    //Forward Rendering
    public bool useReflectionProbes = true;
    [Range(0, 2)]
    public float reflectionProbeIntensity = 0.5f;
    [Range(0, 2)]
    public float reflectionProbeAttribution = 1f;
    public LayerMask reflectionProbeLayerMask = 2147483647;

    //Delayed voxelization
    public bool updateVoxelsAfterXDoUpdate = false;
    public int updateVoxelsAfterXInterval = 1;
    private double updateVoxelsAfterXPrevX = 9223372036854775807;
    private double updateVoxelsAfterXPrevY = 9223372036854775807;
    private double updateVoxelsAfterXPrevZ = 9223372036854775807;
    
    public void LoadAndApplyPreset(string path)
	{
		SEGICascadedPreset preset = Resources.Load<SEGICascadedPreset>(path);

		ApplyPreset(preset);
	}

	public void ApplyPreset(SEGICascadedPreset preset)
	{
        voxelResolution = preset.voxelResolution;
        voxelAA = preset.voxelAA;
		innerOcclusionLayers = preset.innerOcclusionLayers;
		infiniteBounces = preset.infiniteBounces;

		temporalBlendWeight = preset.temporalBlendWeight;
		useBilateralFiltering = preset.useBilateralFiltering;
        GIResolution = preset.GIResolution;
        stochasticSampling = preset.stochasticSampling;
        doReflections = preset.doReflections;

		cones = preset.cones;
		coneTraceSteps = preset.coneTraceSteps;
		coneLength = preset.coneLength;
		coneWidth = preset.coneWidth;
		coneTraceBias = preset.coneTraceBias;
		occlusionStrength = preset.occlusionStrength;
		nearOcclusionStrength = preset.nearOcclusionStrength;
		occlusionPower = preset.occlusionPower;
		nearLightGain = preset.nearLightGain;
		giGain = preset.giGain;
		secondaryBounceGain = preset.secondaryBounceGain;

		reflectionSteps = preset.reflectionSteps;
		reflectionOcclusionPower = preset.reflectionOcclusionPower;
		skyReflectionIntensity = preset.skyReflectionIntensity;
		gaussianMipFilter = preset.gaussianMipFilter;

		farOcclusionStrength = preset.farOcclusionStrength;
		farthestOcclusionStrength = preset.farthestOcclusionStrength;
		secondaryCones = preset.secondaryCones;
		secondaryOcclusionStrength = preset.secondaryOcclusionStrength;
	}

	void Start()
	{
		InitCheck();
	}

	void InitCheck()
	{
		if (initChecker == null)
		{
			Init();
		}
	}

    void CreateVolumeTextures()
    {
        volumeTextures = new RenderTexture[numMipLevels];

        for (int i = 0; i < numMipLevels; i++)
        {
            if (volumeTextures[i])
            {
                //volumeTextures[i].DiscardContents();
                volumeTextures[i].Release();
                //DestroyImmediate(volumeTextures[i]);
            }
            int resolution = (int)voxelResolution / Mathf.RoundToInt(Mathf.Pow((float)2, (float)i));
            volumeTextures[i] = new RenderTexture(resolution, resolution, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Default);
            volumeTextures[i].isVolume = true;
            volumeTextures[i].volumeDepth = resolution;
            volumeTextures[i].enableRandomWrite = true;
            volumeTextures[i].filterMode = FilterMode.Bilinear;
            volumeTextures[i].generateMips = false;
            volumeTextures[i].useMipMap = false;
            volumeTextures[i].Create();
            volumeTextures[i].hideFlags = HideFlags.HideAndDontSave;
        }

        if (volumeTextureB)
        {
            //volumeTextureB.DiscardContents();
            volumeTextureB.Release();
            //DestroyImmediate(volumeTextureB);
        }
        volumeTextureB = new RenderTexture((int)voxelResolution, (int)voxelResolution, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Default);
        volumeTextureB.isVolume = true;
        volumeTextureB.volumeDepth = (int)voxelResolution;
        volumeTextureB.enableRandomWrite = true;
        volumeTextureB.filterMode = FilterMode.Bilinear;
        volumeTextureB.generateMips = false;
        volumeTextureB.useMipMap = false;
        volumeTextureB.Create();
        volumeTextureB.hideFlags = HideFlags.HideAndDontSave;

        if (volumeTexture1)
        {
            //volumeTexture1.DiscardContents();
            volumeTexture1.Release();
            //DestroyImmediate(volumeTexture1);
        }
        volumeTexture1 = new RenderTexture((int)voxelResolution, (int)voxelResolution, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Default);
        volumeTexture1.isVolume = true;
        volumeTexture1.volumeDepth = (int)voxelResolution;
        volumeTexture1.enableRandomWrite = true;
        volumeTexture1.filterMode = FilterMode.Point;
        volumeTexture1.generateMips = false;
        volumeTexture1.useMipMap = false;
        volumeTexture1.antiAliasing = 1;
        volumeTexture1.Create();
        volumeTexture1.hideFlags = HideFlags.HideAndDontSave;



        if (intTex1)
        {
            //intTex1.DiscardContents();
            intTex1.Release();
            //DestroyImmediate(intTex1);
        }
        intTex1 = new RenderTexture((int)voxelResolution, (int)voxelResolution, 0, RenderTextureFormat.RInt, RenderTextureReadWrite.Default);
        intTex1.isVolume = true;
        intTex1.volumeDepth = (int)voxelResolution;
        intTex1.enableRandomWrite = true;
        intTex1.filterMode = FilterMode.Point;
        intTex1.Create();
        intTex1.hideFlags = HideFlags.HideAndDontSave;

        ResizeDummyTexture();

    }

    void ResizeDummyTexture()
	{
		if (dummyVoxelTexture)
		{
			//dummyVoxelTexture.DiscardContents();
			dummyVoxelTexture.Release();
			//DestroyImmediate(dummyVoxelTexture);
		}
		dummyVoxelTexture = new RenderTexture(dummyVoxelResolution, dummyVoxelResolution, 0, RenderTextureFormat.R8);
		dummyVoxelTexture.Create();
		dummyVoxelTexture.hideFlags = HideFlags.HideAndDontSave;

		if (dummyVoxelTexture2)
		{
			//dummyVoxelTexture2.DiscardContents();
			dummyVoxelTexture2.Release();
			//DestroyImmediate(dummyVoxelTexture2);
		}
		dummyVoxelTexture2 = new RenderTexture((int)voxelResolution, (int)voxelResolution, 0, RenderTextureFormat.R8);
		dummyVoxelTexture2.Create();
		dummyVoxelTexture2.hideFlags = HideFlags.HideAndDontSave;
	}

    void Init()
    {
        //Gaussian Filter
        Gaussian_Shader = Shader.Find("Hidden/SEGI Gaussian Blur Filter");
        Gaussian_Material = new Material(Gaussian_Shader);
        Gaussian_Material.enableInstancing = true;

        //FXAA
        FXAA_Shader = Shader.Find("Hidden/SEGIFXAA");
        FXAA_Material = new Material(FXAA_Shader);
        FXAA_Material.enableInstancing = true;
        FXAA_Material.SetFloat("_ContrastThreshold", 0.063f);
        FXAA_Material.SetFloat("_RelativeThreshold", 0.063f);
        FXAA_Material.SetFloat("_SubpixelBlending", 1f);
        FXAA_Material.DisableKeyword("LUMINANCE_GREEN");

        //Setup shaders and materials
        sunDepthShader = Shader.Find("Hidden/SEGIRenderSunDepth_C");
        clearCompute = Resources.Load("SEGIClear_C") as ComputeShader;
        transferInts = Resources.Load("SEGITransferInts_C") as ComputeShader;
        mipFilter = Resources.Load("SEGIMipFilter_C") as ComputeShader;
        voxelizationShader = Shader.Find("Hidden/SEGIVoxelizeScene_C");
        voxelTracingShader = Shader.Find("Hidden/SEGITraceScene_C");

        if (!material)
        {
            material = new Material(Shader.Find("Hidden/SEGI_C"));
            material.enableInstancing = true;
            material.hideFlags = HideFlags.HideAndDontSave;
        }

        //Get the camera attached to this game object
        attachedCamera = this.GetComponent<Camera>();
        attachedCamera.depthTextureMode |= DepthTextureMode.Depth;
        attachedCamera.depthTextureMode |= DepthTextureMode.DepthNormals;
#if UNITY_5_4_OR_NEWER
        attachedCamera.depthTextureMode |= DepthTextureMode.MotionVectors;
#endif

        //Find the proxy reflection render probe if it exists
        GameObject rfgo = GameObject.Find("SEGI_REFLECTIONPROBE");

        //If not, create it
        if (!rfgo)
        {
            reflectionProbeGameObject = new GameObject("SEGI_REFLECTIONPROBE");
            reflectionProbe = reflectionProbeGameObject.AddComponent<ReflectionProbe>();
            reflectionProbeGameObject.hideFlags = HideFlags.HideAndDontSave;

            reflectionProbeGameObject.transform.parent = attachedCamera.transform;
            reflectionProbe.timeSlicingMode = ReflectionProbeTimeSlicingMode.IndividualFaces;
            reflectionProbe.refreshMode = ReflectionProbeRefreshMode.ViaScripting;
            reflectionProbe.clearFlags = ReflectionProbeClearFlags.SolidColor;
            reflectionProbe.cullingMask = reflectionProbeLayerMask;
            reflectionProbe.size = new Vector3(updateVoxelsAfterXInterval * 2.5f, updateVoxelsAfterXInterval * 2.5f, updateVoxelsAfterXInterval * 2.5f);
            reflectionProbe.mode = ReflectionProbeMode.Realtime;
            reflectionProbe.shadowDistance = voxelSpaceSize;
            reflectionProbe.farClipPlane = voxelSpaceSize;
            reflectionProbe.backgroundColor = Color.black;
            reflectionProbe.boxProjection = true;
            
            reflectionProbe.resolution = 128;
            reflectionProbe.importance = 0;
            reflectionProbe.enabled = true;
            reflectionProbe.hdr = false;
        }

            //Find the proxy shadow rendering camera if it exists
            GameObject scgo = GameObject.Find("SEGI_SHADOWCAM");

            if (!scgo)
            {
                shadowCamGameObject = new GameObject("SEGI_SHADOWCAM");
                shadowCam = shadowCamGameObject.AddComponent<Camera>();
                shadowCamGameObject.hideFlags = HideFlags.HideAndDontSave;


                shadowCam.enabled = false;
                shadowCam.depth = attachedCamera.depth - 1;
                shadowCam.orthographic = true;
                shadowCam.orthographicSize = shadowSpaceSize;
                shadowCam.clearFlags = CameraClearFlags.SolidColor;
                shadowCam.backgroundColor = new Color(0.0f, 0.0f, 0.0f, 1.0f);
                shadowCam.farClipPlane = shadowSpaceSize * 2.0f * shadowSpaceDepthRatio;
                shadowCam.stereoTargetEye = StereoTargetEyeMask.None;
                shadowCam.cullingMask = giCullingMask;
                shadowCam.useOcclusionCulling = false;

                shadowCamTransform = shadowCamGameObject.transform;
            }
            else
            {
                shadowCamGameObject = scgo;
                shadowCam = scgo.GetComponent<Camera>();
                shadowCamTransform = shadowCamGameObject.transform;
            }

            if (sunDepthTexture)
            {
                //sunDepthTexture.DiscardContents();
                sunDepthTexture.Release();
                //DestroyImmediate(sunDepthTexture);
            }
            sunDepthTexture = new RenderTexture(sunShadowResolution, sunShadowResolution, 24, RenderTextureFormat.RHalf, RenderTextureReadWrite.Default);
            sunDepthTexture.wrapMode = TextureWrapMode.Clamp;
            sunDepthTexture.filterMode = FilterMode.Point;
            sunDepthTexture.Create();
            sunDepthTexture.hideFlags = HideFlags.HideAndDontSave;



        //Get blue noise textures
        blueNoise = null;
        blueNoise = new Texture2D[64];
        for (int i = 0; i < 64; i++)
        {
            string fileName = "LDR_RGBA_" + i.ToString();
            Texture2D blueNoiseTexture = Resources.Load("Noise Textures/" + fileName) as Texture2D;

            if (blueNoiseTexture == null)
            {
                Debug.LogWarning("Unable to find noise texture \"Assets/SEGI/Resources/Noise Textures/" + fileName + "\" for SEGI!");
            }

            blueNoise[i] = blueNoiseTexture;

        }

            CreateVolumeTextures();



            GameObject vcgo = GameObject.Find("SEGI_VOXEL_CAMERA");
            if (vcgo)
                DestroyImmediate(vcgo);

            voxelCameraGO = new GameObject("SEGI_VOXEL_CAMERA");
            voxelCameraGO.hideFlags = HideFlags.HideAndDontSave;

            voxelCamera = voxelCameraGO.AddComponent<Camera>();
            voxelCamera.enabled = false;
            voxelCamera.orthographic = true;
            voxelCamera.orthographicSize = voxelSpaceSize * 0.5f;
            voxelCamera.nearClipPlane = 0.0f;
            voxelCamera.farClipPlane = voxelSpaceSize;
            voxelCamera.depth = -2;
            voxelCamera.renderingPath = RenderingPath.Forward;
            voxelCamera.clearFlags = CameraClearFlags.Color;
            voxelCamera.backgroundColor = Color.black;
            voxelCamera.useOcclusionCulling = false;

            GameObject lvp = GameObject.Find("SEGI_LEFT_VOXEL_VIEW");
            if (lvp)
                DestroyImmediate(lvp);

            leftViewPoint = new GameObject("SEGI_LEFT_VOXEL_VIEW");
            leftViewPoint.hideFlags = HideFlags.HideAndDontSave;

            GameObject tvp = GameObject.Find("SEGI_TOP_VOXEL_VIEW");
            if (tvp)
                DestroyImmediate(tvp);

            topViewPoint = new GameObject("SEGI_TOP_VOXEL_VIEW");
            topViewPoint.hideFlags = HideFlags.HideAndDontSave;


            CreateVolumeTextures();

            //CommandBuffer
            if (SEGIBuffer == null)
            {
                SEGIBuffer = new CommandBuffer();
                SEGIBuffer.name = "SEGI Render Loop";

            }
            //attachedCamera.AddCommandBuffer(CameraEvent.BeforeImageEffectsOpaque, SEGIBuffer);

            initChecker = new object();
        
    }

	void CheckSupport()
	{
		systemSupported.hdrTextures = SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.ARGBHalf);
		systemSupported.rIntTextures = SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.RInt);
		systemSupported.dx11 = SystemInfo.graphicsShaderLevel >= 50 && SystemInfo.supportsComputeShaders;
		systemSupported.volumeTextures = SystemInfo.supports3DTextures;

		systemSupported.postShader = material.shader.isSupported;
		systemSupported.sunDepthShader = sunDepthShader.isSupported;
		systemSupported.voxelizationShader = voxelizationShader.isSupported;
		systemSupported.tracingShader = voxelTracingShader.isSupported;

		if (!systemSupported.fullFunctionality)
		{
			Debug.LogWarning("SEGI is not supported on the current platform. Check for shader compile errors in SEGI/Resources");
			enabled = false;
		}
	}

	void OnDrawGizmosSelected()
	{
		Color prevColor = Gizmos.color;
		Gizmos.color = new Color(1.0f, 0.25f, 0.0f, 0.5f);

		Gizmos.DrawCube(voxelSpaceOrigin, new Vector3(voxelSpaceSize, voxelSpaceSize, voxelSpaceSize));

		Gizmos.color = new Color(1.0f, 0.0f, 0.0f, 0.1f);

		Gizmos.color = prevColor;
	}

	/*void CleanupTexture(ref RenderTexture texture)
	{
		DestroyImmediate(texture);

	}*/

	void CleanupTextures()
	{
		if (sunDepthTexture) sunDepthTexture.Release();
		if (previousResult) previousResult.Release();
		if (previousDepth) previousDepth.Release();
		if (intTex1) intTex1.Release();
		for (int i = 0; i < volumeTextures.Length; i++)
		{
			if (volumeTextures[i]) volumeTextures[i].Release();
		}
		volumeTexture1.Release();
		volumeTextureB.Release();
		dummyVoxelTexture.Release();
		dummyVoxelTexture2.Release();
        SEGIRenderSource.Release();
        SEGIRenderDestination.Release();

        RT_FXAART.Release();
        RT_gi1.Release();
        RT_gi2.Release();
        RT_reflections.Release();
        RT_gi3.Release();
        RT_gi4.Release();
        RT_blur0.Release();
        RT_blur1.Release();
        RT_FXAARTluminance.Release();
    }

    void Cleanup()
    {
        DestroyImmediate(material);
        DestroyImmediate(voxelCameraGO);
        DestroyImmediate(leftViewPoint);
        DestroyImmediate(topViewPoint);
        DestroyImmediate(shadowCamGameObject);
        DestroyImmediate(reflectionProbeGameObject);
        initChecker = null;
        CleanupTextures();

    /*if (SEGIBuffer != null)
    {
          attachedCamera.RemoveCommandBuffer(CameraEvent.BeforeImageEffectsOpaque, SEGIBuffer);
    }*/
}

	void OnEnable()
	{
		InitCheck();
		ResizeRenderTextures();

		CheckSupport();
	}

	void OnDisable()
	{
		Cleanup();
	}

    public void ResizeRenderTextures()
    {
        if (previousResult) previousResult.Release();
        //StopCoroutine(updateVoxels());

        if (SEGIRenderWidth == 0) SEGIRenderWidth = attachedCamera.scaledPixelWidth;
        if (SEGIRenderHeight == 0) SEGIRenderHeight = attachedCamera.scaledPixelHeight;

        RenderTextureDescriptor RT_Disc0 = new RenderTextureDescriptor(SEGIRenderWidth, SEGIRenderHeight, renderTextureFormat, 24);
        RenderTextureDescriptor RT_Disc1 = new RenderTextureDescriptor(SEGIRenderWidth / (int)giRenderRes, SEGIRenderHeight, renderTextureFormat, 24);
         
        SEGIRenderWidth = attachedCamera.pixelWidth == 0 ? 2 : attachedCamera.pixelWidth;
		SEGIRenderHeight = attachedCamera.pixelHeight == 0 ? 2 : attachedCamera.pixelHeight;

		previousResult = new RenderTexture(RT_Disc0);
		previousResult.wrapMode = TextureWrapMode.Clamp;
		previousResult.filterMode = FilterMode.Bilinear;
		previousResult.useMipMap = true;
		previousResult.generateMips = true;
		previousResult.Create();
		previousResult.hideFlags = HideFlags.HideAndDontSave;

        if (previousDepth)
        {
            //previousDepth.DiscardContents();
            previousDepth.Release();
            //DestroyImmediate(previousDepth);
        }
        previousDepth = new RenderTexture(RT_Disc0);
        previousDepth.wrapMode = TextureWrapMode.Clamp;
        previousDepth.filterMode = FilterMode.Bilinear;
        previousDepth.Create();
        previousDepth.hideFlags = HideFlags.HideAndDontSave;

        if (SEGIRenderSource) SEGIRenderSource.Release();
        SEGIRenderSource = new RenderTexture(RT_Disc0);
        if (attachedCamera.stereoEnabled) SEGIRenderSource.vrUsage = VRTextureUsage.TwoEyes;
        SEGIRenderSource.wrapMode = TextureWrapMode.Clamp;
        SEGIRenderSource.filterMode = FilterMode.Point;
        SEGIRenderSource.Create();
        SEGIRenderSource.hideFlags = HideFlags.HideAndDontSave;

        if (SEGIRenderDestination) SEGIRenderDestination.Release();
        SEGIRenderDestination = new RenderTexture(RT_Disc0);
        if (UnityEngine.XR.XRSettings.enabled) SEGIRenderDestination.vrUsage = VRTextureUsage.TwoEyes;
        SEGIRenderDestination.wrapMode = TextureWrapMode.Clamp;
        SEGIRenderDestination.filterMode = FilterMode.Point;
        SEGIRenderDestination.Create();
        SEGIRenderDestination.hideFlags = HideFlags.HideAndDontSave;

        if (RT_FXAART) RT_FXAART.Release();
        RT_FXAART = new RenderTexture(RT_Disc0);
        if (UnityEngine.XR.XRSettings.enabled) RT_FXAART.vrUsage = VRTextureUsage.TwoEyes;
        RT_FXAART.Create();

        if (RT_gi1) RT_gi1.Release();
        RT_gi1 = new RenderTexture(RT_Disc1);
        if (UnityEngine.XR.XRSettings.enabled) RT_gi1.vrUsage = VRTextureUsage.TwoEyes;
        RT_gi1.Create();

        if (RT_gi2) RT_gi2.Release();
        RT_gi2 = new RenderTexture(RT_Disc1);
        if (UnityEngine.XR.XRSettings.enabled) RT_gi2.vrUsage = VRTextureUsage.TwoEyes;
        RT_gi2.Create();

        if (RT_reflections) RT_reflections.Release();
        RT_reflections = new RenderTexture(RT_Disc1);
        if (UnityEngine.XR.XRSettings.enabled) RT_reflections.vrUsage = VRTextureUsage.TwoEyes;
        RT_reflections.Create();

        if (RT_gi3) RT_gi3.Release();
        RT_gi3 = new RenderTexture(RT_Disc0);
        if (UnityEngine.XR.XRSettings.enabled) RT_gi3.vrUsage = VRTextureUsage.TwoEyes;
        RT_gi3.Create();

        if (RT_gi4) RT_gi4.Release();
        RT_gi4 = new RenderTexture(RT_Disc0);
        if (UnityEngine.XR.XRSettings.enabled) RT_gi4.vrUsage = VRTextureUsage.TwoEyes;
        RT_gi4.Create();

        if (RT_blur0) RT_blur0.Release();
        RT_blur0 = new RenderTexture(RT_Disc0);
        if (UnityEngine.XR.XRSettings.enabled) RT_blur0.vrUsage = VRTextureUsage.TwoEyes;
        RT_blur0.Create();

        if (RT_blur1) RT_blur1.Release();
        RT_blur1 = new RenderTexture(RT_Disc0);
        if (UnityEngine.XR.XRSettings.enabled) RT_blur1.vrUsage = VRTextureUsage.TwoEyes;
        RT_blur1.Create();

        if (RT_FXAARTluminance) RT_FXAARTluminance.Release();
        RT_FXAARTluminance = new RenderTexture(RT_Disc0);
        if (UnityEngine.XR.XRSettings.enabled) RT_FXAARTluminance.vrUsage = VRTextureUsage.TwoEyes;
        RT_FXAARTluminance.Create();

        SEGIBufferInit();
        //StartCoroutine(updateVoxels());
    }

	void ResizeSunShadowBuffer()
	{

		if (sunDepthTexture)
		{
			//sunDepthTexture.DiscardContents();
			sunDepthTexture.Release();
			//DestroyImmediate(sunDepthTexture);
		}
		sunDepthTexture = new RenderTexture(sunShadowResolution, sunShadowResolution, 24, RenderTextureFormat.RHalf, RenderTextureReadWrite.Default);
        if (UnityEngine.XR.XRSettings.enabled) sunDepthTexture.vrUsage = VRTextureUsage.TwoEyes;
        sunDepthTexture.wrapMode = TextureWrapMode.Clamp;
		sunDepthTexture.filterMode = FilterMode.Point;
		sunDepthTexture.Create();
		sunDepthTexture.hideFlags = HideFlags.HideAndDontSave;
	}

    void Update()
    {
        return;
        if (notReadyToRender)
            return;

        if (previousResult == null)
        {
            ResizeRenderTextures();
        }

        if (previousResult.width != attachedCamera.pixelWidth || previousResult.height != attachedCamera.pixelHeight)
        {
            ResizeRenderTextures();
        }

        if ((int)sunShadowResolution != prevSunShadowResolution)
        {
            ResizeSunShadowBuffer();
        }

        prevSunShadowResolution = (int)sunShadowResolution;

        if (volumeTextures[0].width != (int)voxelResolution)
        {
            CreateVolumeTextures();
        }

        if (dummyVoxelTexture.width != dummyVoxelResolution)
        {
            ResizeDummyTexture();
        }

    }

    Matrix4x4 TransformViewMatrix(Matrix4x4 mat)
    {
#if UNITY_5_5_OR_NEWER
        if (SystemInfo.usesReversedZBuffer)
        {
            mat[2, 0] = -mat[2, 0];
            mat[2, 1] = -mat[2, 1];
            mat[2, 2] = -mat[2, 2];
            mat[2, 3] = -mat[2, 3];
            // mat[3, 2] += 0.0f;
        }
#endif
        return mat;
    }

    void FixRes(RenderTexture rt)
    {
        if (rt.width != sunShadowResolution || rt.height != sunShadowResolution)
        {
            rt.Release();
            rt.width = rt.height = sunShadowResolution;
            rt.Create();
        }
    }

    public virtual void CustomSunSetup()
    {
        //this gets overridden by external Voxel-Octree class
    }

    public virtual void OnPreRender()
    {
        //Force reinitialization to make sure that everything is working properly if one of the cameras was unexpectedly destroyed
        if (!voxelCamera || !shadowCam)
            initChecker = null;

        InitCheck();

        if (notReadyToRender)
        {
            Debug.Log("<SEGI> (" + name + ") " + "Not Ready to Render");
            return;
        }
            

        if (!updateGI)
        {
            return;
        }

        if (attachedCamera.renderingPath == RenderingPath.Forward && reflectionProbe.enabled)
        {
            reflectionProbe.enabled = true;
            reflectionProbe.intensity = reflectionProbeIntensity;
            reflectionProbe.cullingMask = reflectionProbeLayerMask;
        }
        else
        {
            reflectionProbe.enabled = false;
        }

        // only use main camera for voxel simulations
        if (attachedCamera != Camera.main)
        {
            Debug.Log("<SEGI> (" + name + ") " + "Instance not attached to Main Camera. Please ensure the attached camera has the 'MainCamera' tag.");
            return;
        }

        //Update SkyColor
        if (MatchAmbiantColor)
        {
            skyColor = RenderSettings.ambientLight;
            skyIntensity = RenderSettings.ambientIntensity;
        }

        //calculationSEGIObject = this;
        //Debug.Log(Camera.current.name + "," + Camera.current.stereoActiveEye + ", " + calculationSEGIObject.name + ", " + Time.frameCount + ", " + Time.renderedFrameCount);
        //Cache the previous active render texture to avoid issues with other Unity rendering going on
        RenderTexture previousActive = RenderTexture.active;
        Shader.SetGlobalInt("SEGIVoxelAA", voxelAA ? 1 : 0);

        CustomSunSetup();

        //Temporarily disable rendering of shadows on the directional light during voxelization pass. Cache the result to set it back to what it was after voxelization is done
        LightShadows prevSunShadowSetting = LightShadows.None;
        if (sun != null)
        {
            prevSunShadowSetting = sun.shadows;
            sun.shadows = LightShadows.None;
        }


        Shader.SetGlobalMatrix("WorldToGI", shadowCam.worldToCameraMatrix);
        Shader.SetGlobalMatrix("GIToWorld", shadowCam.cameraToWorldMatrix);
        Shader.SetGlobalMatrix("GIProjection", shadowCam.projectionMatrix);
        Shader.SetGlobalMatrix("GIProjectionInverse", shadowCam.projectionMatrix.inverse);
        Shader.SetGlobalMatrix("WorldToCamera", attachedCamera.worldToCameraMatrix);
        Shader.SetGlobalFloat("GIDepthRatio", shadowSpaceDepthRatio);

        Shader.SetGlobalColor("GISunColor", sun == null ? Color.black : new Color(Mathf.Pow(sun.color.r, 2.2f), Mathf.Pow(sun.color.g, 2.2f), Mathf.Pow(sun.color.b, 2.2f), Mathf.Pow(sun.intensity, 2.2f)));
        Shader.SetGlobalColor("SEGISkyColor", new Color(Mathf.Pow(skyColor.r * skyIntensity * 0.5f, 2.2f), Mathf.Pow(skyColor.g * skyIntensity * 0.5f, 2.2f), Mathf.Pow(skyColor.b * skyIntensity * 0.5f, 2.2f), Mathf.Pow(skyColor.a, 2.2f)));
        Shader.SetGlobalFloat("GIGain", giGain);

        Shader.SetGlobalInt("SEGIVoxelResolution", (int)voxelResolution);

        Shader.SetGlobalMatrix("SEGIVoxelViewFront", TransformViewMatrix(voxelCamera.transform.worldToLocalMatrix));
        Shader.SetGlobalMatrix("SEGIVoxelViewLeft", TransformViewMatrix(leftViewPoint.transform.worldToLocalMatrix));
        Shader.SetGlobalMatrix("SEGIVoxelViewTop", TransformViewMatrix(topViewPoint.transform.worldToLocalMatrix));
        Shader.SetGlobalMatrix("SEGIWorldToVoxel", voxelCamera.worldToCameraMatrix);
        Shader.SetGlobalMatrix("SEGIVoxelProjection", voxelCamera.projectionMatrix);
        Shader.SetGlobalMatrix("SEGIVoxelProjectionInverse", voxelCamera.projectionMatrix.inverse);

        Shader.SetGlobalFloat("SEGISecondaryBounceGain", infiniteBounces ? secondaryBounceGain : 0.0f);
        Shader.SetGlobalFloat("SEGISoftSunlight", softSunlight);
        Shader.SetGlobalInt("SEGISphericalSkylight", sphericalSkylight ? 1 : 0);
        Shader.SetGlobalInt("SEGIInnerOcclusionLayers", innerOcclusionLayers);

        Matrix4x4 voxelToGIProjection = (shadowCam.projectionMatrix) * (shadowCam.worldToCameraMatrix) * (voxelCamera.cameraToWorldMatrix);
        Shader.SetGlobalMatrix("SEGIVoxelToGIProjection", voxelToGIProjection);
        Shader.SetGlobalVector("SEGISunlightVector", sun ? Vector3.Normalize(sun.transform.forward) : Vector3.up);


        if (attachedCamera.transform.position.x - updateVoxelsAfterXPrevX >= updateVoxelsAfterXInterval) updateVoxelsAfterXDoUpdate = true;
        if (updateVoxelsAfterXPrevX - attachedCamera.transform.position.x >= updateVoxelsAfterXInterval) updateVoxelsAfterXDoUpdate = true;

        if (attachedCamera.transform.position.y - updateVoxelsAfterXPrevY >= updateVoxelsAfterXInterval) updateVoxelsAfterXDoUpdate = true;
        if (updateVoxelsAfterXPrevY - attachedCamera.transform.position.y >= updateVoxelsAfterXInterval) updateVoxelsAfterXDoUpdate = true;

        if (attachedCamera.transform.position.z - updateVoxelsAfterXPrevZ >= updateVoxelsAfterXInterval) updateVoxelsAfterXDoUpdate = true;
        if (updateVoxelsAfterXPrevZ - attachedCamera.transform.position.z >= updateVoxelsAfterXInterval) updateVoxelsAfterXDoUpdate = true;


        if (renderState == RenderState.Voxelize && updateVoxelsAfterXDoUpdate == true)
        {
            //voxelCamera.rect = new Rect(0f, 0f, 0.5f, 0.5f);

            activeVolume = voxelFlipFlop == 0 ? volumeTextures[0] : volumeTextureB;
            previousActiveVolume = voxelFlipFlop == 0 ? volumeTextureB : volumeTextures[0];

            float voxelTexel = (1.0f * voxelSpaceSize) / (int)voxelResolution * 0.5f;

            float interval = voxelSpaceSize / 8.0f;
            Vector3 origin;
            if (followTransform)
            {
                origin = followTransform.position;
            }
            else
            {
                origin = transform.position + transform.forward * voxelSpaceSize / 4.0f;
            }
            voxelSpaceOrigin = new Vector3(Mathf.Round(origin.x / interval) * interval, Mathf.Round(origin.y / interval) * interval, Mathf.Round(origin.z / interval) * interval) + new Vector3(1.0f, 1.0f, 1.0f) * ((float)voxelFlipFlop * 2.0f - 1.0f) * voxelTexel * 0.0f;

            voxelSpaceOriginDelta = voxelSpaceOrigin - previousVoxelSpaceOrigin;
            Shader.SetGlobalVector("SEGIVoxelSpaceOriginDelta", voxelSpaceOriginDelta / voxelSpaceSize);

            previousVoxelSpaceOrigin = voxelSpaceOrigin;

            if (sun != null)
            {
                shadowCam.cullingMask = giCullingMask;

                Vector3 shadowCamPosition = voxelSpaceOrigin + Vector3.Normalize(-sun.transform.forward) * shadowSpaceSize * 0.5f * shadowSpaceDepthRatio;

                shadowCamTransform.position = shadowCamPosition;
                shadowCamTransform.LookAt(voxelSpaceOrigin, Vector3.up);

                shadowCam.renderingPath = RenderingPath.Forward;
                shadowCam.depthTextureMode |= DepthTextureMode.None;

                shadowCam.orthographicSize = shadowSpaceSize;
                shadowCam.farClipPlane = shadowSpaceSize * 2.0f * shadowSpaceDepthRatio;

                Graphics.SetRenderTarget(sunDepthTexture);
                if (!UnityEngine.XR.XRSettings.enabled) shadowCam.SetTargetBuffers(sunDepthTexture.colorBuffer, sunDepthTexture.depthBuffer);

                shadowCam.RenderWithShader(sunDepthShader, "");

                Shader.SetGlobalTexture("SEGISunDepth", sunDepthTexture);
            }


            voxelCamera.enabled = false;
            voxelCamera.orthographic = true;
            voxelCamera.orthographicSize = voxelSpaceSize * 0.5f;
            voxelCamera.nearClipPlane = 0.0f;
            voxelCamera.farClipPlane = voxelSpaceSize;
            voxelCamera.depth = -2;
            voxelCamera.stereoTargetEye = StereoTargetEyeMask.None;
            voxelCamera.renderingPath = RenderingPath.Forward;
            voxelCamera.clearFlags = CameraClearFlags.Color;
            voxelCamera.backgroundColor = Color.black;
            voxelCamera.cullingMask = giCullingMask;
            



            voxelFlipFlop += 1;
            voxelFlipFlop = voxelFlipFlop % 2;

            voxelCameraGO.transform.position = voxelSpaceOrigin - Vector3.forward * voxelSpaceSize * 0.5f;
            voxelCameraGO.transform.rotation = rotationFront;

            leftViewPoint.transform.position = voxelSpaceOrigin + Vector3.left * voxelSpaceSize * 0.5f;
            leftViewPoint.transform.rotation = rotationLeft;
            topViewPoint.transform.position = voxelSpaceOrigin + Vector3.up * voxelSpaceSize * 0.5f;
            topViewPoint.transform.rotation = rotationTop;

            clearCompute.SetTexture(0, "RG0", intTex1);
            clearCompute.SetInt("Res", (int)voxelResolution);
            clearCompute.Dispatch(0, (int)voxelResolution / 16, (int)voxelResolution / 16, 1);


            Graphics.SetRandomWriteTarget(1, intTex1);
            voxelCamera.targetTexture = dummyVoxelTexture;
            voxelCamera.RenderWithShader(voxelizationShader, "");
            Graphics.ClearRandomWriteTargets();

            transferInts.SetTexture(0, "Result", activeVolume);
            transferInts.SetTexture(0, "PrevResult", previousActiveVolume);
            transferInts.SetTexture(0, "RG0", intTex1);
            transferInts.SetInt("VoxelAA", voxelAA ? 1 : 0);
            transferInts.SetInt("Resolution", (int)voxelResolution);
            transferInts.SetVector("VoxelOriginDelta", (voxelSpaceOriginDelta / voxelSpaceSize) * (int)voxelResolution);
            transferInts.Dispatch(0, (int)voxelResolution / 16, (int)voxelResolution / 16, 1);

            Shader.SetGlobalTexture("SEGIVolumeLevel0", activeVolume);

            for (int i = 0; i < numMipLevels - 1; i++)
            {
                RenderTexture source = volumeTextures[i];

                if (i == 0)
                {
                    source = activeVolume;
                }

                int destinationRes = (int)voxelResolution / Mathf.RoundToInt(Mathf.Pow((float)2, (float)i + 1.0f));
                mipFilter.SetInt("destinationRes", destinationRes);
                mipFilter.SetTexture(mipFilterKernel, "Source", source);
                mipFilter.SetTexture(mipFilterKernel, "Destination", volumeTextures[i + 1]);
                mipFilter.Dispatch(mipFilterKernel, (int)voxelResolution / 16, (int)voxelResolution / 16, 1);
                Shader.SetGlobalTexture("SEGIVolumeLevel" + (i + 1).ToString(), volumeTextures[i + 1]);
            }

            if (infiniteBounces)
            {
                renderState = RenderState.Bounce;
            }
            else
            {
                updateVoxelsAfterXDoUpdate = false;
                updateVoxelsAfterXPrevX = attachedCamera.transform.position.x;
                updateVoxelsAfterXPrevY = attachedCamera.transform.position.y;
                updateVoxelsAfterXPrevZ = attachedCamera.transform.position.z;
            }
        }
        else if (renderState == RenderState.Bounce && updateVoxelsAfterXDoUpdate == true);
        {

            clearCompute.SetTexture(0, "RG0", intTex1);
            clearCompute.Dispatch(0, (int)voxelResolution / 16, (int)voxelResolution / 16, 1);

            Shader.SetGlobalInt("SEGISecondaryCones", secondaryCones);
            Shader.SetGlobalFloat("SEGISecondaryOcclusionStrength", secondaryOcclusionStrength);

            Graphics.SetRandomWriteTarget(1, intTex1);
            voxelCamera.targetTexture = dummyVoxelTexture2;
            voxelCamera.RenderWithShader(voxelTracingShader, "");
            Graphics.ClearRandomWriteTargets();

            transferInts.SetTexture(1, "Result", volumeTexture1);
            transferInts.SetTexture(1, "RG0", intTex1);
            transferInts.SetInt("Resolution", (int)voxelResolution);
            transferInts.Dispatch(1, (int)voxelResolution / 16, (int)voxelResolution / 16, 1);

            Shader.SetGlobalTexture("SEGIVolumeTexture1", volumeTexture1);

            renderState = RenderState.Voxelize;
        }
        //voxelCamera.rect = new Rect(0.0f, 0f, 1.0f, 1.0f);

        Matrix4x4 giToVoxelProjection = voxelCamera.projectionMatrix * voxelCamera.worldToCameraMatrix * shadowCam.cameraToWorldMatrix;
        Shader.SetGlobalMatrix("GIToVoxelProjection", giToVoxelProjection);

        //Fix stereo rendering matrix
        if (attachedCamera.stereoEnabled)
        {
            // Left and Right Eye inverse View Matrices
            Matrix4x4 leftToWorld = attachedCamera.GetStereoViewMatrix(Camera.StereoscopicEye.Left).inverse;
            Matrix4x4 rightToWorld = attachedCamera.GetStereoViewMatrix(Camera.StereoscopicEye.Right).inverse;
            material.SetMatrix("_LeftEyeToWorld", leftToWorld);
            material.SetMatrix("_RightEyeToWorld", rightToWorld);

            Matrix4x4 leftEye = attachedCamera.GetStereoProjectionMatrix(Camera.StereoscopicEye.Left);
            Matrix4x4 rightEye = attachedCamera.GetStereoProjectionMatrix(Camera.StereoscopicEye.Right);

            // Compensate for RenderTexture...
            leftEye = GL.GetGPUProjectionMatrix(leftEye, true).inverse;
            rightEye = GL.GetGPUProjectionMatrix(rightEye, true).inverse;
            // Negate [1,1] to reflect Unity's CBuffer state
            leftEye[1, 1] *= -1;
            rightEye[1, 1] *= -1;

            material.SetMatrix("_LeftEyeProjection", leftEye);
            material.SetMatrix("_RightEyeProjection", rightEye);
        }
        //Fix stereo rendering matrix/

        RenderTexture.active = previousActive;

        //Set the sun's shadow setting back to what it was before voxelization
        if (sun != null)
        {
            sun.shadows = prevSunShadowSetting;
        }
    }
    
    IEnumerator<int> updateVoxels()
    {
        /*
        while (true)
        {
            /*for (int i = 0; i < numMipLevels - 1; i++)
            {
                RenderTexture source = activeVolume;

                if (i == 0)
                {
                    source = activeVolume;
                }

                int destinationRes = (int)voxelResolution / Mathf.RoundToInt(Mathf.Pow((float)2, (float)i + 1.0f));
                mipFilter.SetInt("destinationRes", destinationRes);
                mipFilter.SetTexture(mipFilterKernel, "Source", source);
                mipFilter.SetTexture(mipFilterKernel, "Destination", volumeTextures[i + 1]);
                mipFilter.Dispatch(mipFilterKernel, (int)voxelResolution / 16, (int)voxelResolution / 16, 1);
                Shader.SetGlobalTexture("SEGIVolumeLevel" + (i + 1).ToString(), volumeTextures[i + 1]);
                yield return 0;
            }
            yield return 0;
        }*/
        yield break;
    }

    [ImageEffectOpaque]
    public void OnRenderImage(RenderTexture source, RenderTexture destination)
    {
        if (notReadyToRender)
        {
            Graphics.Blit(source, destination);
            return;
        }

        if (visualizeSunDepthTexture && sunDepthTexture != null && sunDepthTexture != null)
        {
            Graphics.Blit(sunDepthTexture, destination);
            return;
        }

        Shader.SetGlobalFloat("SEGIVoxelScaleFactor", voxelScaleFactor);

        material.SetMatrix("CameraToWorld", attachedCamera.cameraToWorldMatrix);
        material.SetMatrix("WorldToCamera", attachedCamera.worldToCameraMatrix);
        material.SetMatrix("ProjectionMatrixInverse", attachedCamera.projectionMatrix.inverse);
        material.SetMatrix("ProjectionMatrix", attachedCamera.projectionMatrix);
        material.SetInt("FrameSwitch", frameSwitch);
        Shader.SetGlobalInt("SEGIFrameSwitch", frameSwitch);
        material.SetVector("CameraPosition", transform.position);
        material.SetFloat("DeltaTime", Time.deltaTime);

        material.SetInt("StochasticSampling", stochasticSampling ? 1 : 0);
        material.SetInt("TraceDirections", cones);
        material.SetInt("TraceSteps", coneTraceSteps);
        material.SetFloat("TraceLength", coneLength);
        material.SetFloat("ConeSize", coneWidth);
        material.SetFloat("OcclusionStrength", occlusionStrength);
        material.SetFloat("OcclusionPower", occlusionPower);
        material.SetFloat("ConeTraceBias", coneTraceBias);
        material.SetFloat("GIGain", giGain);
        material.SetFloat("NearLightGain", nearLightGain);
        material.SetFloat("NearOcclusionStrength", nearOcclusionStrength);
        material.SetInt("DoReflections", doReflections ? 1 : 0);
        material.SetInt("GIResolution", GIResolution);
        material.SetInt("ReflectionSteps", reflectionSteps);
        material.SetFloat("ReflectionOcclusionPower", reflectionOcclusionPower);
        material.SetFloat("SkyReflectionIntensity", skyReflectionIntensity);
        material.SetFloat("FarOcclusionStrength", farOcclusionStrength);
        material.SetFloat("FarthestOcclusionStrength", farthestOcclusionStrength);
        material.SetTexture("NoiseTexture", blueNoise[frameSwitch % 64]);
        material.SetFloat("BlendWeight", temporalBlendWeight);
        material.SetInt("useReflectionProbes", useReflectionProbes ? 1 : 0);
        material.SetFloat("reflectionProbeIntensity", reflectionProbeIntensity);
        material.SetFloat("reflectionProbeAttribution", reflectionProbeAttribution); 
        material.SetInt("StereoEnabled", UnityEngine.XR.XRSettings.enabled ? 1 : 0);

        if (SEGIRenderWidth != source.width || SEGIRenderHeight != source.height)
        {
            SEGIRenderWidth = source.width;
            SEGIRenderHeight = source.height;
            SEGIBufferInit();
        }

        Graphics.Blit(source, SEGIRenderSource);
        Graphics.ExecuteCommandBuffer(SEGIBuffer);
        Graphics.Blit(SEGIRenderDestination, destination, material);

        material.SetMatrix("ProjectionPrev", attachedCamera.projectionMatrix);
        material.SetMatrix("ProjectionPrevInverse", attachedCamera.projectionMatrix.inverse);
        material.SetMatrix("WorldToCameraPrev", attachedCamera.worldToCameraMatrix);
        material.SetMatrix("CameraToWorldPrev", attachedCamera.cameraToWorldMatrix);
        material.SetVector("CameraPositionPrev", transform.position);

        frameSwitch = (frameSwitch + 1) % (128);
       //material.ref
    }

    struct ExecuteSEGIBufer : IJob
    {
        //public NativeArray<Material.> result;

        public void Execute()
        {

        }
    }

    public void SEGIBufferInit()
    {
        //CommandBuffer
        if (SEGIBuffer != null) SEGIBuffer.Clear();
        else return;

        updateVoxelsAfterXPrevX = 9223372036854775807;
        updateVoxelsAfterXPrevY = 9223372036854775807;
        updateVoxelsAfterXPrevZ = 9223372036854775807;

        //Blit once to downsample if required
        SEGIBuffer.Blit(SEGIRenderSource, RT_gi1);

        if (attachedCamera.renderingPath == RenderingPath.Forward)
        {
            SEGIBuffer.SetGlobalInt("ForwardPath", 1);
            SEGIBuffer.SetGlobalTexture("_Albedo", SEGIRenderSource);
        }
        else SEGIBuffer.SetGlobalInt("ForwardPath", 0);

        //If Visualize Voxels is enabled, just render the voxel visualization shader pass and return
        if (visualizeVoxels)
        {
            SEGIBuffer.Blit(SEGIRenderSource, SEGIRenderDestination, material, Pass.VisualizeVoxels);
            return;
        }

        //Set the previous GI result and camera depth textures to access them in the shader
        SEGIBuffer.SetGlobalTexture("PreviousGITexture", previousResult);
        SEGIBuffer.SetGlobalTexture("PreviousDepth", previousDepth);

        //Render diffuse GI tracing result
        SEGIBuffer.Blit(RT_gi1, RT_gi2, material, Pass.DiffuseTrace);

        //Render GI reflections result
        if (doReflections)
        {
            SEGIBuffer.Blit(RT_gi1, RT_reflections, material, Pass.SpecularTrace);
            SEGIBuffer.SetGlobalTexture("Reflections", RT_reflections);
        }

        //If Half Resolution tracing is enabled
        if (giRenderRes >= 2)
        {
            //Prepare the half-resolution diffuse GI result to be bilaterally upsampled
            //SEGIBuffer.Blit(SEGICMDBufferRT.gi2, SEGICMDBufferRT.gi4);

            //Perform bilateral upsampling on half-resolution diffuse GI result
            SEGIBuffer.SetGlobalVector("Kernel", new Vector2(1.0f, 0.0f));
            SEGIBuffer.Blit(RT_gi2, RT_gi3, material, Pass.BilateralUpsample);
            SEGIBuffer.SetGlobalVector("Kernel", new Vector2(0.0f, 1.0f));

            //Perform a bilateral blur to be applied in newly revealed areas that are still noisy due to not having previous data blended with it

            //material.SetVector("Kernel", new Vector2(0.0f, 1.0f));
            //SEGIBuffer.Blit(RT_gi3, RT_blur0, material, Pass.BilateralBlur);
            //material.SetVector("Kernel", new Vector2(1.0f, 0.0f));
            //SEGIBuffer.Blit(RT_blur0, RT_gi3, material, Pass.BilateralBlur);

            SEGIBuffer.Blit(SEGICMDBufferRT.gi3, SEGICMDBufferRT.blur0, Gaussian_Material);
            SEGIBuffer.Blit(SEGICMDBufferRT.blur0, SEGICMDBufferRT.gi3, Gaussian_Material);
            SEGIBuffer.SetGlobalTexture("BlurredGI", RT_blur0);

            //Perform temporal reprojection and blending
            if (temporalBlendWeight < 1.0f)
            {
                SEGIBuffer.Blit(RT_gi3, RT_gi4, material, Pass.TemporalBlend);
                //SEGIBuffer.Blit(SEGICMDBufferRT.gi4, SEGICMDBufferRT.gi3, material, Pass.TemporalBlend);
                SEGIBuffer.Blit(RT_gi4, previousResult);
                SEGIBuffer.Blit(RT_gi1, previousDepth, material, Pass.GetCameraDepthTexture);
            }

            if (GIResolution >= 3)
            {
                //material.SetVector("Kernel", new Vector2(0.0f, 1.0f));
                //SEGIBuffer.Blit(RT_gi3, RT_gi4, material, Pass.BilateralBlur);
                //material.SetVector("Kernel", new Vector2(1.0f, 0.0f));
                //SEGIBuffer.Blit(RT_gi4, RT_gi3, material, Pass.BilateralBlur);

                SEGIBuffer.Blit(SEGICMDBufferRT.gi3, SEGICMDBufferRT.gi4, Gaussian_Material);
                SEGIBuffer.Blit(SEGICMDBufferRT.gi4, SEGICMDBufferRT.gi3, Gaussian_Material);
            }

            //Set the result to be accessed in the shader
            SEGIBuffer.SetGlobalTexture("GITexture", RT_gi3);

            //Actually apply the GI to the scene using gbuffer data
            SEGIBuffer.Blit(SEGIRenderSource, RT_FXAART, material, visualizeGI ? Pass.VisualizeGI : Pass.BlendWithScene);
        }
        else    //If Half Resolution tracing is disabled
        {

            if (temporalBlendWeight < 1.0f)
            {
                //Perform a bilateral blur to be applied in newly revealed areas that are still noisy due to not having previous data blended with it
                //material.SetVector("Kernel", new Vector2(0.0f, 1.0f));
                //SEGIBuffer.Blit(RT_gi2, RT_blur1, material, Pass.BilateralBlur);
                //material.SetVector("Kernel", new Vector2(1.0f, 0.0f));
                //SEGIBuffer.Blit(RT_blur1, RT_blur0, material, Pass.BilateralBlur);

                SEGIBuffer.Blit(SEGICMDBufferRT.gi2, SEGICMDBufferRT.blur1, Gaussian_Material);
                SEGIBuffer.Blit(SEGICMDBufferRT.blur1, SEGICMDBufferRT.blur0, Gaussian_Material);
                SEGIBuffer.SetGlobalTexture("BlurredGI", RT_blur0);

                //Perform temporal reprojection and blending
                SEGIBuffer.Blit(RT_gi2, RT_gi1, material, Pass.TemporalBlend);
                SEGIBuffer.Blit(RT_gi1, previousResult);
                SEGIBuffer.Blit(RT_gi1, previousDepth, material, Pass.GetCameraDepthTexture);
            }

            //Actually apply the GI to the scene using gbuffer data
            SEGIBuffer.SetGlobalTexture("GITexture", RT_gi2);
            SEGIBuffer.Blit(SEGIRenderSource, RT_FXAART, material, visualizeGI ? Pass.VisualizeGI : Pass.BlendWithScene);
        }
        if (useFXAA)
        {
            SEGIBuffer.Blit(RT_FXAART, RT_FXAARTluminance, FXAA_Material, 0);
            SEGIBuffer.Blit(RT_FXAARTluminance, SEGIRenderDestination, FXAA_Material, 1);
        }
        else SEGIBuffer.Blit(RT_FXAART, SEGIRenderDestination);

        //ENDCommandBuffer


        //Advance the frame counter
        frameSwitch = (frameSwitch + 1) % (64);
    }

    float[] Vector4ArrayToFloats(Vector4[] vecArray)
    {
        float[] temp = new float[vecArray.Length * 4];
        for (int i = 0; i < vecArray.Length; i++)
        {
            temp[i * 4 + 0] = vecArray[i].x;
            temp[i * 4 + 1] = vecArray[i].y;
            temp[i * 4 + 2] = vecArray[i].z;
            temp[i * 4 + 3] = vecArray[i].w;
        }
        return temp;
    }

    float[] MatrixArrayToFloats(Matrix4x4[] mats)
    {
        float[] temp = new float[mats.Length * 16];
        for (int i = 0; i < mats.Length; i++)
        {
            for (int n = 0; n < 16; n++)
            {
                temp[i * 16 + n] = mats[i][n];
            }
        }
        return temp;
    }

    float[] MatrixToFloats(Matrix4x4 mat)
    {
        float[] temp = new float[16];
        for (int i = 0; i < 16; i++)
        {
            temp[i] = mat[i];
        }
        return temp;
    }
    float[] MatrixToFloats(Matrix4x4 mat, bool transpose)
    {
        Matrix4x4 matTranspose = mat;
        if (transpose)
            matTranspose = Matrix4x4.Transpose(mat);
        float[] temp = new float[16];
        for (int i = 0; i < 16; i++)
        {
            temp[i] = matTranspose[i];
        }
        return temp;
    }

    static int[] FindTextureSize(int pCellCount)
    {
        if (pCellCount <= 0)
        {
            Debug.LogError("pCellCount has to be > 0");
            return null;
        }
        int size = pCellCount;
        while (size != 1)
        {
            if (size % 2 != 0)
            {
                Debug.LogError("pCellCount is not a power of two");
                return null;
            }
            size /= 2;
        }
        int repeat_x = 2;
        int repeat_y = 0;
        while (true)
        {
            size = repeat_x * pCellCount;
            while (size != 1)
            {
                if (size % 2 != 0)
                {
                    break;
                }
                size /= 2;
            }
            if (size == 1)
            { //if it is a power of two size is 1
                repeat_y = pCellCount / repeat_x;
                if (pCellCount % repeat_x != 0)
                    repeat_y++;
                if (repeat_y <= repeat_x)
                {
                    return new int[]
                    {
                            repeat_x * pCellCount,
                            repeat_y * pCellCount,
                            repeat_x,
                            repeat_y
                    };
                }
            }
            repeat_x++;
        }
    }
}

