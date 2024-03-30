using HarmonyLib;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UniGLTF;
using UnityEngine;
using VRM;

namespace ValheimVRM
{
	[HarmonyPatch(typeof(Shader))]
	[HarmonyPatch(nameof(Shader.Find))]
	static class ShaderPatch
	{
		static bool Prefix(ref Shader __result, string name)
		{
			if (VRMShaders.Shaders.TryGetValue(name, out var shader))
			{
				__result = shader;
				return false;
			}

			return true;
		}
	}

	public static class VRMShaders
	{
		public static Dictionary<string, Shader> Shaders { get; } = new Dictionary<string, Shader>();

		public static void Initialize()
		{
			var bundlePath = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), @"ValheimVRM.shaders");
			if (File.Exists(bundlePath))
			{
				var assetBundle = AssetBundle.LoadFromFile(bundlePath);
				var assets = assetBundle.LoadAllAssets<Shader>();
				foreach (var asset in assets)
				{
					UnityEngine.Debug.Log("[ValheimVRM] Add Shader: " + asset.name);
					Shaders.Add(asset.name, asset);
				}
			}
		}
	}

	public static class VRMModels
	{
		public static Dictionary<string, byte[]> VrmBufDic = new Dictionary<string, byte[]>();
		public static Dictionary<Player, GameObject> PlayerToVrmDic = new Dictionary<Player, GameObject>();
		public static Dictionary<Player, string> PlayerToNameDic = new Dictionary<Player, string>();
		public static Dictionary<Character, GameObject> NpcToVrmDic = new Dictionary<Character, GameObject>();
		public static Dictionary<Character, string> NpcToNameDic = new Dictionary<Character, string>();
	}

	[HarmonyPatch(typeof(VisEquipment), "UpdateLodgroup")]
	static class Patch_VisEquipment_UpdateLodgroup
	{
		[HarmonyPostfix]
		static void Postfix(VisEquipment __instance)
		{
			if (!__instance.m_isPlayer) return;

			var humanoid = __instance.GetComponent<Humanoid>();
			if (humanoid == null) return;
			var player = humanoid as Player;
			string name = null;
			if (player == null)
			{
				// Not a player, see if it is an NPC.
				var npc = humanoid as Character;
				if (npc == null || !VRMModels.NpcToVrmDic.ContainsKey(npc)) return;
				name = npc.m_name;
			}
			else
			{
				if (!VRMModels.PlayerToVrmDic.ContainsKey(player)) return;
				name = VRMModels.PlayerToNameDic[player];
			}
			var hair = __instance.GetField<VisEquipment, GameObject>("m_hairItemInstance");
			if (hair != null) SetVisible(hair, false);

			var beard = __instance.GetField<VisEquipment, GameObject>("m_beardItemInstance");
			if (beard != null) SetVisible(beard, false);

			var chestList = __instance.GetField<VisEquipment, List<GameObject>>("m_chestItemInstances");
			if (chestList != null) foreach (var chest in chestList) SetVisible(chest, false);

			var legList = __instance.GetField<VisEquipment, List<GameObject>>("m_legItemInstances");
			if (legList != null) foreach (var leg in legList) SetVisible(leg, false);

			var shoulderList = __instance.GetField<VisEquipment, List<GameObject>>("m_shoulderItemInstances");
			if (shoulderList != null) foreach (var shoulder in shoulderList) SetVisible(shoulder, false);

			var utilityList = __instance.GetField<VisEquipment, List<GameObject>>("m_utilityItemInstances");
			if (utilityList != null) foreach (var utility in utilityList) SetVisible(utility, false);

			var helmet = __instance.GetField<VisEquipment, GameObject>("m_helmetItemInstance");
			if (helmet != null) SetVisible(helmet, false);

			// 武器位置合わせ

			var leftItem = __instance.GetField<VisEquipment, GameObject>("m_leftItemInstance");
			if (leftItem != null) leftItem.transform.localPosition = Settings.ReadVector3(name, "LeftHandEuqipPos", Vector3.zero);

			var rightItem = __instance.GetField<VisEquipment, GameObject>("m_rightItemInstance");
			if (rightItem != null) rightItem.transform.localPosition = Settings.ReadVector3(name, "RightHandEuqipPos", Vector3.zero);
			
			// divided  by 100 to keep the settings file positions in the same number range. (position offset appears to be on the world, not local)
			var rightBackItem = __instance.GetField<VisEquipment, GameObject>("m_rightBackItemInstance");
			if (rightBackItem != null) rightBackItem.transform.localPosition = Settings.ReadVector3(name, "RightHandBackItemPos", Vector3.zero) / 100.0f;
			
			var leftBackItem = __instance.GetField<VisEquipment, GameObject>("m_leftBackItemInstance");
			if (leftBackItem != null) leftBackItem.transform.localPosition = Settings.ReadVector3(name, "LeftHandBackItemPos", Vector3.zero) / 100.0f;
		}

		private static void SetVisible(GameObject obj, bool flag)
		{
			foreach (var mr in obj.GetComponentsInChildren<MeshRenderer>()) mr.enabled = flag;
			foreach (var smr in obj.GetComponentsInChildren<SkinnedMeshRenderer>()) smr.enabled = flag;
		}
	}

	[HarmonyPatch(typeof(Humanoid), "OnRagdollCreated")]
	static class Patch_Humanoid_OnRagdollCreated
	{
		[HarmonyPostfix]
		static void Postfix(Humanoid __instance, Ragdoll ragdoll)
		{
			foreach (var smr in ragdoll.GetComponentsInChildren<SkinnedMeshRenderer>())
			{
				smr.forceRenderingOff = true;
				smr.updateWhenOffscreen = true;
			}

			var ragAnim = ragdoll.gameObject.AddComponent<Animator>();
			ragAnim.keepAnimatorControllerStateOnDisable = true;
			ragAnim.cullingMode = AnimatorCullingMode.AlwaysAnimate;

			if (__instance.IsPlayer())
			{
				var orgAnim = ((Player)__instance).GetField<Player, Animator>("m_animator");
				ragAnim.avatar = orgAnim.avatar;

				if (VRMModels.PlayerToVrmDic.TryGetValue((Player)__instance, out var vrm))
				{
					vrm.transform.SetParent(ragdoll.transform);
					vrm.GetComponent<VRMAnimationSync>().Setup(ragAnim, true);
				}
			}
			else {
				var orgAnim = ((Character)__instance).GetField<Character, Animator>("m_animator");
				ragAnim.avatar = orgAnim.avatar;

				if (VRMModels.NpcToVrmDic.TryGetValue((Character)__instance, out var vrm))
				{
					vrm.transform.SetParent(ragdoll.transform);
					vrm.GetComponent<VRMAnimationSync>().Setup(ragAnim, true);
				}
			}
		}
	}

	[HarmonyPatch(typeof(Character), "SetVisible")]
	static class Patch_Character_SetVisible
	{
		[HarmonyPostfix]
		static void Postfix(Character __instance, bool visible)
		{
			if (__instance.IsPlayer())
			{
				if (VRMModels.PlayerToVrmDic.TryGetValue((Player)__instance, out var vrm))
				{
					var lodGroup = vrm.GetComponent<LODGroup>();
					if (visible)
					{
						lodGroup.localReferencePoint = __instance.GetField<Character, Vector3>("m_originalLocalRef");
					}
					else
					{
						lodGroup.localReferencePoint = new Vector3(999999f, 999999f, 999999f);
					}
				}
			}
			else {
				if (VRMModels.NpcToVrmDic.TryGetValue(__instance, out var vrm))
				{
					var lodGroup = vrm.GetComponent<LODGroup>();
					if (visible)
					{
						lodGroup.localReferencePoint = __instance.GetField<Character, Vector3>("m_originalLocalRef");
					}
					else
					{
						lodGroup.localReferencePoint = new Vector3(999999f, 999999f, 999999f);
					}
				}
			}
		}
	}

	[HarmonyPatch(typeof(Player), "OnDeath")]
	static class Patch_Player_OnDeath
	{
		[HarmonyPostfix]
		static void Postfix(Player __instance)
		{
			string name = null;
			if (VRMModels.PlayerToNameDic.ContainsKey(__instance)) name = VRMModels.PlayerToNameDic[__instance];
			if (name != null && Settings.ReadBool(name, "FixCameraHeight", true))
			{
				GameObject.Destroy(__instance.GetComponent<VRMEyePositionSync>());
			}
		}
	}

	[HarmonyPatch(typeof(Character), "GetHeadPoint")]
	static class Patch_Character_GetHeadPoint
	{
		[HarmonyPostfix]
		static bool Prefix(Character __instance, ref Vector3 __result)
		{
			var player = __instance as Player;
			if (player != null)
			{

				if (VRMModels.PlayerToVrmDic.TryGetValue(player, out var vrm))
				{
					var animator = vrm.GetComponentInChildren<Animator>();
					if (animator == null) return true;

					var head = animator.GetBoneTransform(HumanBodyBones.Head);
					if (head == null) return true;

					__result = head.position;
					return false;
				}
			}
			else {
				if (VRMModels.NpcToVrmDic.TryGetValue(__instance, out var vrm))
				{
					var animator = vrm.GetComponentInChildren<Animator>();
					if (animator == null) return true;

					var head = animator.GetBoneTransform(HumanBodyBones.Head);
					if (head == null) return true;

					__result = head.position;
					return false;
				}
			}
			
			return true;
		}
	}



	[HarmonyPatch(typeof(Player), "Awake")]
	static class Patch_Player_Awake
	{
		private static Dictionary<string, GameObject> vrmDic = new Dictionary<string, GameObject>();

		[HarmonyPostfix]
		static void Postfix(Player __instance)
		{
			string playerName = null;
			if (Game.instance != null)
			{
				playerName = __instance.GetPlayerName();
				if (playerName == "" || playerName == "...") playerName = Game.instance.GetPlayerProfile().GetName();
			}
			else
			{
				var index = FejdStartup.instance.GetField<FejdStartup, int>("m_profileIndex");
				var profiles = FejdStartup.instance.GetField<FejdStartup, List<PlayerProfile>>("m_profiles");
				if (index >= 0 && index < profiles.Count) playerName = profiles[index].GetName();
			}

			if (!string.IsNullOrEmpty(playerName) && !vrmDic.ContainsKey(playerName))
			{
				var path = Path.Combine(Environment.CurrentDirectory, "ValheimVRM", $"{playerName}.vrm");

				ref var m_nview = ref AccessTools.FieldRefAccess<Player, ZNetView>("m_nview").Invoke(__instance);
				if (!File.Exists(path))
				{
					Debug.LogError("[ValheimVRM] VRMファイルが見つかりません.");
					Debug.LogError("[ValheimVRM] 読み込み予定だったVRMファイルパス: " + path);
				}
				else
				{
					if (!Settings.ContainsSettings(playerName))
					{
						if (!Settings.AddSettingsFromFile(playerName))
						{
							Debug.LogWarning("[ValheimVRM] 設定ファイルが見つかりません.以下の設定ファイルが存在するか確認してください: " + Settings.PlayerSettingsPath(playerName));
						}
					}

					var scale = Settings.ReadFloat(playerName, "ModelScale", 1.1f);
					var orgVrm =  ImportVRM(path, scale);
					if (orgVrm != null)
					{
						GameObject.DontDestroyOnLoad(orgVrm);
						vrmDic[playerName] = orgVrm;
						VRMModels.VrmBufDic[playerName] = File.ReadAllBytes(path);

						//[Error: Unity Log] _Cutoff: Range
						//[Error: Unity Log] _MainTex: Texture
						//[Error: Unity Log] _SkinBumpMap: Texture
						//[Error: Unity Log] _SkinColor: Color
						//[Error: Unity Log] _ChestTex: Texture
						//[Error: Unity Log] _ChestBumpMap: Texture
						//[Error: Unity Log] _ChestMetal: Texture
						//[Error: Unity Log] _LegsTex: Texture
						//[Error: Unity Log] _LegsBumpMap: Texture
						//[Error: Unity Log] _LegsMetal: Texture
						//[Error: Unity Log] _BumpScale: Float
						//[Error: Unity Log] _Glossiness: Range
						//[Error: Unity Log] _MetalGlossiness: Range

						// シェーダ差し替え
						var brightness = Settings.ReadFloat(playerName, "ModelBrightness", 0.8f);
						var materials = new List<Material>();
						foreach (var smr in orgVrm.GetComponentsInChildren<SkinnedMeshRenderer>())
						{
							foreach (var mat in smr.materials)
							{
								if (!materials.Contains(mat)) materials.Add(mat);
							}
						}
						foreach (var mr in orgVrm.GetComponentsInChildren<MeshRenderer>())
						{
							foreach (var mat in mr.materials)
							{
								if (!materials.Contains(mat)) materials.Add(mat);
							}
						}

						if (Settings.ReadBool(playerName, "UseMToonShader", false))
						{
							foreach (var mat in materials)
							{
								if (mat.HasProperty("_Color"))
								{
									var color = mat.GetColor("_Color");
									color.r *= brightness;
									color.g *= brightness;
									color.b *= brightness;
									mat.SetColor("_Color", color);
								}
							}
						}
						else
						{
							var shader = Shader.Find("Custom/Player");
							foreach (var mat in materials)
							{
								if (mat.shader == shader) continue;

								var color = mat.HasProperty("_Color") ? mat.GetColor("_Color") : Color.white;

								var mainTex = mat.HasProperty("_MainTex") ? mat.GetTexture("_MainTex") as Texture2D : null;
								Texture2D tex = mainTex;
								if (mainTex != null)
								{
									tex = new Texture2D(mainTex.width, mainTex.height);
									var colors = mainTex.GetPixels();
									for (var i = 0; i < colors.Length; i++)
									{
										var col = colors[i] * color;
										float h, s, v;
										Color.RGBToHSV(col, out h, out s, out v);
										v *= brightness;
										colors[i] = Color.HSVToRGB(h, s, v);
										colors[i].a = col.a;
									}
									tex.SetPixels(colors);
									tex.Apply();
								}

								var bumpMap = mat.HasProperty("_BumpMap") ? mat.GetTexture("_BumpMap") : null;
								mat.shader = shader;

								mat.SetTexture("_MainTex", tex);
								mat.SetTexture("_SkinBumpMap", bumpMap);
								mat.SetColor("_SkinColor", color);
								mat.SetTexture("_ChestTex", tex);
								mat.SetTexture("_ChestBumpMap", bumpMap);
								mat.SetTexture("_LegsTex", tex);
								mat.SetTexture("_LegsBumpMap", bumpMap);
								mat.SetFloat("_Glossiness", 0.2f);
								mat.SetFloat("_MetalGlossiness", 0.0f);
								
							}
						}

						var lodGroup = orgVrm.AddComponent<LODGroup>();
						var lod = new LOD(0.1f, orgVrm.GetComponentsInChildren<SkinnedMeshRenderer>());
						if (Settings.ReadBool(playerName, "EnablePlayerFade", true)) lodGroup.SetLODs(new LOD[] { lod });
						lodGroup.RecalculateBounds();

						var orgLodGroup = __instance.GetComponentInChildren<LODGroup>();
						lodGroup.fadeMode = orgLodGroup.fadeMode;
						lodGroup.animateCrossFading = orgLodGroup.animateCrossFading;

						orgVrm.SetActive(false);
					}
				}
			}

			if (!string.IsNullOrEmpty(playerName) && vrmDic.ContainsKey(playerName))
			{
				var vrmModel = GameObject.Instantiate(vrmDic[playerName]);
				VRMModels.PlayerToVrmDic[__instance] = vrmModel;
				VRMModels.PlayerToNameDic[__instance] = playerName;
				vrmModel.SetActive(true);
				vrmModel.transform.SetParent(__instance.GetComponentInChildren<Animator>().transform.parent, false);

				foreach (var smr in __instance.GetVisual().GetComponentsInChildren<SkinnedMeshRenderer>())
				{
					smr.forceRenderingOff = true;
					smr.updateWhenOffscreen = true;
				}

				var orgAnim = AccessTools.FieldRefAccess<Player, Animator>(__instance, "m_animator");
				orgAnim.keepAnimatorControllerStateOnDisable = true;
				orgAnim.cullingMode = AnimatorCullingMode.AlwaysAnimate;

				vrmModel.transform.localPosition = orgAnim.transform.localPosition;

				// アニメーション同期
				var offsetY = Settings.ReadFloat(playerName, "ModelOffsetY");
				if (vrmModel.GetComponent<VRMAnimationSync>() == null) vrmModel.AddComponent<VRMAnimationSync>().Setup(orgAnim, false, offsetY);
				else vrmModel.GetComponent<VRMAnimationSync>().Setup(orgAnim, false, offsetY);

				// カメラ位置調整
				if (Settings.ReadBool(playerName, "FixCameraHeight", true))
				{
					var vrmEye = vrmModel.GetComponent<Animator>().GetBoneTransform(HumanBodyBones.LeftEye);
					if (vrmEye == null) vrmEye = vrmModel.GetComponent<Animator>().GetBoneTransform(HumanBodyBones.Head);
					if (vrmEye == null) vrmEye = vrmModel.GetComponent<Animator>().GetBoneTransform(HumanBodyBones.Neck);
					if (vrmEye != null)
					{
						if (__instance.gameObject.GetComponent<VRMEyePositionSync>() == null) __instance.gameObject.AddComponent<VRMEyePositionSync>().Setup(vrmEye);
						else __instance.gameObject.GetComponent<VRMEyePositionSync>().Setup(vrmEye);
					}
				}

				// MToonの場合環境光の影響をカラーに反映する
				if (Settings.ReadBool(playerName, "UseMToonShader", false))
				{
					if (vrmModel.GetComponent<MToonColorSync>() == null) vrmModel.AddComponent<MToonColorSync>().Setup(vrmModel);
					else vrmModel.GetComponent<MToonColorSync>().Setup(vrmModel);
				}

				// SpringBone設定
				var stiffness = Settings.ReadFloat(playerName, "SpringBoneStiffness", 1.0f);
				var gravity = Settings.ReadFloat(playerName, "SpringBoneGravityPower", 1.0f);
				foreach (var springBone in vrmModel.GetComponentsInChildren<VRM.VRMSpringBone>())
				{
					springBone.m_stiffnessForce *= stiffness;
					springBone.m_gravityPower *= gravity;
					springBone.m_updateType = VRMSpringBone.SpringBoneUpdateType.FixedUpdate;
					springBone.m_center = null;
				}
			}
		}

		private static GameObject ImportVRM(string path, float scale)
		{
			try
			{
				var data = new GlbFileParser(path).Parse();
				var vrm = new VRMData(data);
				var context = new VRMImporterContext(vrm);
				var loaded = default(RuntimeGltfInstance);
				loaded = context.Load();
				loaded.ShowMeshes();
				loaded.Root.transform.localScale *= scale;

				Debug.Log("[ValheimVRM] VRM読み込み成功");
				Debug.Log("[ValheimVRM] VRMファイルパス: " + path);

				return loaded.Root;
			}
			catch (Exception ex)
			{
				Debug.LogError(ex);
			}

			return null;
		}
	}

	[HarmonyPatch(typeof(Character), "Awake")]
	static class Patch_Npc_Awake
	{
		private static Dictionary<string, GameObject> vrmDic = new Dictionary<string, GameObject>();

		[HarmonyPostfix]
		static void Postfix(Character __instance)
		{
			if (__instance.IsPlayer() || __instance.GetFaction() != Character.Faction.Players) return;
			string npcName = __instance.m_name;

			if (!string.IsNullOrEmpty(npcName) && !vrmDic.ContainsKey(npcName))
			{
				var path = Path.Combine(Environment.CurrentDirectory, "ValheimVRM", $"{npcName}.vrm");

				ref var m_nview = ref AccessTools.FieldRefAccess<Character, ZNetView>("m_nview").Invoke(__instance);
				if (!File.Exists(path))
				{
					Debug.LogError("[ValheimVRM] VRMファイルが見つかりません.");
					Debug.LogError("[ValheimVRM] 読み込み予定だったVRMファイルパス: " + path);
				}
				else
				{
					if (!Settings.ContainsSettings(npcName))
					{
						if (!Settings.AddSettingsFromFile(npcName))
						{
							Debug.LogWarning("[ValheimVRM] 設定ファイルが見つかりません.以下の設定ファイルが存在するか確認してください: " + Settings.PlayerSettingsPath(npcName));
						}
					}

					var scale = Settings.ReadFloat(npcName, "ModelScale", 1.1f);
					var orgVrm = ImportVRM(path, scale);
					if (orgVrm != null)
					{
						GameObject.DontDestroyOnLoad(orgVrm);
						vrmDic[npcName] = orgVrm;
						VRMModels.VrmBufDic[npcName] = File.ReadAllBytes(path);

						//[Error: Unity Log] _Cutoff: Range
						//[Error: Unity Log] _MainTex: Texture
						//[Error: Unity Log] _SkinBumpMap: Texture
						//[Error: Unity Log] _SkinColor: Color
						//[Error: Unity Log] _ChestTex: Texture
						//[Error: Unity Log] _ChestBumpMap: Texture
						//[Error: Unity Log] _ChestMetal: Texture
						//[Error: Unity Log] _LegsTex: Texture
						//[Error: Unity Log] _LegsBumpMap: Texture
						//[Error: Unity Log] _LegsMetal: Texture
						//[Error: Unity Log] _BumpScale: Float
						//[Error: Unity Log] _Glossiness: Range
						//[Error: Unity Log] _MetalGlossiness: Range

						// シェーダ差し替え
						var brightness = Settings.ReadFloat(npcName, "ModelBrightness", 0.8f);
						var materials = new List<Material>();
						foreach (var smr in orgVrm.GetComponentsInChildren<SkinnedMeshRenderer>())
						{
							foreach (var mat in smr.materials)
							{
								if (!materials.Contains(mat)) materials.Add(mat);
							}
						}
						foreach (var mr in orgVrm.GetComponentsInChildren<MeshRenderer>())
						{
							foreach (var mat in mr.materials)
							{
								if (!materials.Contains(mat)) materials.Add(mat);
							}
						}

						if (Settings.ReadBool(npcName, "UseMToonShader", false))
						{
							foreach (var mat in materials)
							{
								if (mat.HasProperty("_Color"))
								{
									var color = mat.GetColor("_Color");
									color.r *= brightness;
									color.g *= brightness;
									color.b *= brightness;
									mat.SetColor("_Color", color);
								}
							}
						}
						else
						{
							var shader = Shader.Find("Custom/Player");
							foreach (var mat in materials)
							{
								if (mat.shader == shader) continue;

								var color = mat.HasProperty("_Color") ? mat.GetColor("_Color") : Color.white;

								var mainTex = mat.HasProperty("_MainTex") ? mat.GetTexture("_MainTex") as Texture2D : null;
								Texture2D tex = mainTex;
								if (mainTex != null)
								{
									tex = new Texture2D(mainTex.width, mainTex.height);
									var colors = mainTex.GetPixels();
									for (var i = 0; i < colors.Length; i++)
									{
										var col = colors[i] * color;
										float h, s, v;
										Color.RGBToHSV(col, out h, out s, out v);
										v *= brightness;
										colors[i] = Color.HSVToRGB(h, s, v);
										colors[i].a = col.a;
									}
									tex.SetPixels(colors);
									tex.Apply();
								}

								var bumpMap = mat.HasProperty("_BumpMap") ? mat.GetTexture("_BumpMap") : null;
								mat.shader = shader;

								mat.SetTexture("_MainTex", tex);
								mat.SetTexture("_SkinBumpMap", bumpMap);
								mat.SetColor("_SkinColor", color);
								mat.SetTexture("_ChestTex", tex);
								mat.SetTexture("_ChestBumpMap", bumpMap);
								mat.SetTexture("_LegsTex", tex);
								mat.SetTexture("_LegsBumpMap", bumpMap);
								mat.SetFloat("_Glossiness", 0.2f);
								mat.SetFloat("_MetalGlossiness", 0.0f);

							}
						}

						var lodGroup = orgVrm.AddComponent<LODGroup>();
						var lod = new LOD(0.1f, orgVrm.GetComponentsInChildren<SkinnedMeshRenderer>());
						if (Settings.ReadBool(npcName, "EnablePlayerFade", true)) lodGroup.SetLODs(new LOD[] { lod });
						lodGroup.RecalculateBounds();

						var orgLodGroup = __instance.GetComponentInChildren<LODGroup>();
						lodGroup.fadeMode = orgLodGroup.fadeMode;
						lodGroup.animateCrossFading = orgLodGroup.animateCrossFading;

						orgVrm.SetActive(false);
					}
				}
			}

			if (!string.IsNullOrEmpty(npcName) && vrmDic.ContainsKey(npcName))
			{
				var vrmModel = GameObject.Instantiate(vrmDic[npcName]);
				VRMModels.NpcToVrmDic[__instance] = vrmModel;
				VRMModels.NpcToNameDic[__instance] = npcName;
				vrmModel.SetActive(true);
				vrmModel.transform.SetParent(__instance.GetComponentInChildren<Animator>().transform.parent, false);

				foreach (var smr in __instance.GetVisual().GetComponentsInChildren<SkinnedMeshRenderer>())
				{
					smr.forceRenderingOff = true;
					smr.updateWhenOffscreen = true;
				}

				var orgAnim = AccessTools.FieldRefAccess<Character, Animator>(__instance, "m_animator");
				orgAnim.keepAnimatorControllerStateOnDisable = true;
				orgAnim.cullingMode = AnimatorCullingMode.AlwaysAnimate;

				vrmModel.transform.localPosition = orgAnim.transform.localPosition;

				// アニメーション同期
				var offsetY = Settings.ReadFloat(npcName, "ModelOffsetY");
				if (vrmModel.GetComponent<VRMAnimationSync>() == null) vrmModel.AddComponent<VRMAnimationSync>().Setup(orgAnim, false, offsetY);
				else vrmModel.GetComponent<VRMAnimationSync>().Setup(orgAnim, false, offsetY);

				// カメラ位置調整
				if (Settings.ReadBool(npcName, "FixCameraHeight", true))
				{
					var vrmEye = vrmModel.GetComponent<Animator>().GetBoneTransform(HumanBodyBones.LeftEye);
					if (vrmEye == null) vrmEye = vrmModel.GetComponent<Animator>().GetBoneTransform(HumanBodyBones.Head);
					if (vrmEye == null) vrmEye = vrmModel.GetComponent<Animator>().GetBoneTransform(HumanBodyBones.Neck);
					if (vrmEye != null)
					{
						if (__instance.gameObject.GetComponent<VRMEyePositionSync>() == null) __instance.gameObject.AddComponent<VRMEyePositionSync>().Setup(vrmEye);
						else __instance.gameObject.GetComponent<VRMEyePositionSync>().Setup(vrmEye);
					}
				}

				// MToonの場合環境光の影響をカラーに反映する
				if (Settings.ReadBool(npcName, "UseMToonShader", false))
				{
					if (vrmModel.GetComponent<MToonColorSync>() == null) vrmModel.AddComponent<MToonColorSync>().Setup(vrmModel);
					else vrmModel.GetComponent<MToonColorSync>().Setup(vrmModel);
				}

				// SpringBone設定
				var stiffness = Settings.ReadFloat(npcName, "SpringBoneStiffness", 1.0f);
				var gravity = Settings.ReadFloat(npcName, "SpringBoneGravityPower", 1.0f);
				foreach (var springBone in vrmModel.GetComponentsInChildren<VRM.VRMSpringBone>())
				{
					springBone.m_stiffnessForce *= stiffness;
					springBone.m_gravityPower *= gravity;
					springBone.m_updateType = VRMSpringBone.SpringBoneUpdateType.FixedUpdate;
					springBone.m_center = null;
				}

				__instance.GetBaseAI().m_idleSound.m_effectPrefabs[0].m_prefab.name = npcName + "_sfx_idle";
				__instance.GetBaseAI().m_alertedEffects.m_effectPrefabs[0].m_prefab.name = npcName + "_sfx_alert";
				foreach (var prefab in __instance.m_hitEffects.m_effectPrefabs) {
					if (prefab.m_prefab.name == "RRR_NPC_sfx_hit") {
						prefab.m_prefab.name = npcName + "_sfx_hit";
						break;
					}
				}
				foreach (var prefab in __instance.m_deathEffects.m_effectPrefabs)
				{
					if (prefab.m_prefab.name == "RRR_NPC_sfx_death")
					{
						prefab.m_prefab.name = npcName + "_sfx_death";
						break;
					}
				}
				((Humanoid)__instance).m_consumeItemEffects.m_effectPrefabs[0].m_prefab.name = npcName + "_sfx_eat";
			}
		}

		[HarmonyPatch(typeof(Tameable), "Interact")]
		static class Tameable_Interact_Patch
		{
			static bool Prefix(Tameable __instance, ref Character ___m_character)
			{
				if (___m_character.IsPlayer() || ___m_character.GetFaction() != Character.Faction.Players) return false;
				string npcName = ___m_character.m_name;
				if (!string.IsNullOrEmpty(npcName) && !vrmDic.ContainsKey(npcName)) return false;
				if (___m_character.IsTamed()) {
					if (__instance.m_petEffect.m_effectPrefabs.Length > 0 && __instance.m_petEffect.m_effectPrefabs[0].m_prefab.GetComponent<Transform>().childCount > 1)
					{
						__instance.m_petEffect.m_effectPrefabs[0].m_prefab.GetComponent<Transform>().GetChild(1).name = npcName + "_sfx_pet";
					}
				}
				return true;
			}
		}
		private static GameObject ImportVRM(string path, float scale)
		{
			try
			{
				var data = new GlbFileParser(path).Parse();
				var vrm = new VRMData(data);
				var context = new VRMImporterContext(vrm);
				var loaded = default(RuntimeGltfInstance);
				loaded = context.Load();
				loaded.ShowMeshes();
				loaded.Root.transform.localScale *= scale;

				Debug.Log("[ValheimVRM] VRM読み込み成功");
				Debug.Log("[ValheimVRM] VRMファイルパス: " + path);

				return loaded.Root;
			}
			catch (Exception ex)
			{
				Debug.LogError(ex);
			}

			return null;
		}
	}
}
