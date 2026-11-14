using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Alta;
using Alta.Crafting;
using Alta.Inventory;
using Alta.Liquid;
using Alta.Networking;
using Alta.Pages;
using ATT.Saving;
using HarmonyLib;
using MelonLoader;
using SyncLib.Crafting;
using SyncLib.Items;
using UnityEngine;
using UnityEngine.XR;
using Object = UnityEngine.Object;

[assembly: MelonInfo(typeof(CrystalGourd.Mod), "Crystal Gourd", "1.0.4", "ATT")]
[assembly: MelonGame("Alta", "A Township Tale")]
[assembly: MelonAdditionalDependencies("SyncLib")]

namespace CrystalGourd
{
    public sealed class Mod : MelonMod
    {
        internal const string GourdName = "ATT_CrystalGourd", PageName = "ATT_CrystalGourd_Recipe";
        internal const uint StockGourd = 29032, BlueGem = 8002, Buckle = 62594;
        static RegisteredWorkbenchRecipe recipe;
        static GameObject art;

        public override void OnInitializeMelon()
        {
#if GOURD_SERVER
            if (!Application.isBatchMode) return;
#elif GOURD_CLIENT
            if (Application.isBatchMode) return;
#endif
            using (var stream = typeof(Mod).Assembly.GetManifestResourceStream("CrystalGourd.Art"))
            {
                var bundle = AssetBundle.LoadFromStream(stream);
                if (bundle == null) throw new InvalidDataException("Crystal Gourd art bundle could not be opened");
                try { art = bundle.LoadAsset<GameObject>("assets/crystalgourd/generated/crystalgourdart.prefab"); }
                finally { bundle.Unload(false); }
            }
            if (art == null) throw new InvalidDataException("Crystal Gourd art prefab is missing");
            var updateCork = new HarmonyMethod(typeof(CrystalCork), nameof(CrystalCork.UpdateVisibility));
            HarmonyInstance.Patch(AccessTools.DeclaredMethod(typeof(LiquidContainerLid), "InternalCloseLid"), postfix: updateCork);
            HarmonyInstance.Patch(AccessTools.DeclaredMethod(typeof(LiquidContainerLid), "InternalRemoveLid"), postfix: updateCork);
            if (!Application.isBatchMode)
            {
                StereoMaterials.Install(art);
                var applyFinish = new HarmonyMethod(typeof(WorkbenchFinish), nameof(WorkbenchFinish.Apply));
                HarmonyInstance.Patch(AccessTools.DeclaredMethod(typeof(GenericCraftingPiece), nameof(GenericCraftingPiece.SetMaterial)), postfix: applyFinish);
                HarmonyInstance.Patch(AccessTools.DeclaredMethod(typeof(GenericCraftingPiece), nameof(GenericCraftingPiece.ApplySaveData)), postfix: applyFinish);
                HarmonyInstance.Patch(AccessTools.DeclaredMethod(typeof(PickupDockingChecker), "GetClosestDock"),
                    postfix: new HarmonyMethod(typeof(PotionHoopTarget), nameof(PotionHoopTarget.FindByNeck)));
            }
            NetworkPrefabRegistry.RegisterPrefabs += RegisterPrefabs;
        }

        public override void OnDeinitializeMelon()
        {
            NetworkPrefabRegistry.RegisterPrefabs -= RegisterPrefabs;
            WorkbenchFinish.Clear();
            StereoMaterials.Dispose();
        }

        void RegisterPrefabs()
        {
            if (recipe != null) return;
            var stock = PrefabManager.GetPrefab(StockGourd);
            var gem = PrefabManager.GetPrefab(BlueGem);
            var buckle = PrefabManager.GetPrefab(Buckle);
            if (new[]{stock,gem,buckle}.Any(p=>p==null || p.GetComponent<Pickup>()==null))
                throw new InvalidOperationException("Crystal Gourd native donors are missing");

            var metal = buckle.GetComponent<GenericCraftingComponent>();
            if (metal == null || metal.Material == null) throw new InvalidOperationException("Native buckle crafting is unavailable");
            var staging = new GameObject("Crystal Gourd prefab templates");
            staging.SetActive(false);
            Object.DontDestroyOnLoad(staging);
            var gourd = Object.Instantiate(stock.gameObject, staging.transform, false);
            gourd.name = GourdName;
            SetupGourd(gourd);
            var result = this.RegisterCustomPrefab(gourd, GourdName);
            if (result == null) { Object.Destroy(staging); return; }
            result.SaveMode = SaveMode.Base;
            Set(result, "canBeSpawnedThroughCommand", true);
            Bind(gourd);
            var glyph = MakeGlyph();
            var item = RegisterItem(stock, result, glyph);
            RegisterPotionHoops(item);

            EnableGemCrafting(gem, metal);
            recipe = RegisterRecipe(result, glyph, gem.GetComponent<Pickup>().Item);
            gourd.SetActive(true);
            LoggerInstance.Msg($"CRYSTAL_GOURD_READY item={result.Hash} page={recipe.Page.Hash} recipe={recipe.RecipeHash} servings=3");
        }

        static void SetupGourd(GameObject gourd)
        {
            PrepareRigidbody(gourd);
            var container = gourd.GetComponent<LiquidContainer>();
            Set(container,"containerSize",3);
            Set(container,"liquidDistribution",null);
            var liquid = container.SurfaceVisual;
            var liquidRenderer = liquid.DynamicRenderer;
            liquidRenderer.GetComponent<MeshFilter>().sharedMesh = art.transform.Find("Liquid").GetComponent<MeshFilter>().sharedMesh;
            var retainedLiquidLods = (Renderer[])Field(typeof(LiquidSurfaceVisual),"otherLods").GetValue(liquid);
            foreach(var lower in retainedLiquidLods.Where(r=>r!=null)) lower.enabled=false;
            Set(liquid,"otherLods",new Renderer[0]);
            Object.DestroyImmediate(gourd.GetComponent<LODGroup>());
            foreach(var renderer in gourd.GetComponentsInChildren<MeshRenderer>(true))
                if (renderer != liquidRenderer) renderer.enabled=false;

            var lid = gourd.GetComponent<LiquidContainerLid>();
            var cork = (CurveScaler)Field(typeof(LiquidContainerLid),"fakeCork").GetValue(lid);
            Set(lid,"isClosedByDefualtIfEmpty",false);
            Set(gourd.GetComponent<AutoContainerLid>(),"isClosingIfEmpty",false);
            var mouth = art.transform.Find("Mouth").localPosition;
            cork.transform.SetPositionAndRotation(gourd.transform.TransformPoint(mouth), gourd.transform.rotation);
            var modelCork = Object.Instantiate(art.transform.Find("Cork").gameObject,cork.transform,false);
            var corkScale = cork.transform.lossyScale;
            var bodyScale = gourd.transform.lossyScale;
            modelCork.transform.localScale = new Vector3(bodyScale.x / corkScale.x, bodyScale.y / corkScale.y, bodyScale.z / corkScale.z);
            gourd.AddComponent<CrystalCork>().Configure(container, lid, modelCork);
            var body = Object.Instantiate(art.transform.Find("Body").gameObject,gourd.transform,false);
            var crystalVisibility = body.GetComponentInChildren<MeshRenderer>(true).gameObject.AddComponent<VisibilityDetector>();
            Set(crystalVisibility,"becameVisible",new AltaEvent());
            Set(crystalVisibility,"becameInvisible",new AltaEvent());
            Set(liquid,"containerLod0",crystalVisibility);
            Set(liquid,"androidContainerLod0",crystalVisibility);
            var cap = gourd.GetComponentInChildren<CapPickup>(true);
            var neck = art.transform.Find("Neck Direction").localPosition;
            Set(cap,"popDirection",cap.transform.InverseTransformDirection(gourd.transform.TransformDirection((mouth-neck).normalized)));
            Set(cap,"popDistance",.075f);
            FitPickup(gourd.GetComponent<Pickup>(), cap, body.transform.Find("LOD0"), modelCork.transform);
            var pour = gourd.GetComponent<PourableContainer>();
            Set(pour,"containerLipRadius",.020f);
            var opening = (Transform)Field(typeof(PourableContainer),"pouringParticlesParent").GetValue(pour);
            opening.position = gourd.transform.TransformPoint(mouth);
        }

        static void FitPickup(Pickup pickup, CapPickup cap, Transform detail, Transform modelCork)
        {
            var children = Field(typeof(Pickup), "childrenColliders");
            var capColliders = (Collider[])children.GetValue(cap);
            var stock = (Collider[])children.GetValue(pickup);
            var body = stock.Where(c => c != null && !capColliders.Contains(c)).ToArray();
            int tightLayer = LayerMask.NameToLayer("Tight Grip");
            var solid = body.First(c => c.gameObject.layer != tightLayer && !c.isTrigger);
            var tight = body.FirstOrDefault(c => c.gameObject.layer == tightLayer);
            var chambers = detail.GetComponentsInChildren<MeshFilter>(true).Where(m => m.name.StartsWith("Chamber ", StringComparison.Ordinal)).ToArray();
            var frame = art.transform.Find("Collision").GetComponentsInChildren<MeshFilter>(true);
            var replacement = new List<Collider>(capColliders);
            foreach (var filter in chambers.Concat(frame))
                replacement.Add(AddHull(pickup.transform, solid, filter));
            if (tight != null)
                foreach (var filter in chambers.Concat(frame.Where(m => m.name == "Cap" || m.name == "handle")))
                    replacement.Add(AddHull(pickup.transform, tight, filter));
            children.SetValue(pickup, replacement.ToArray());
            foreach (var old in body) Object.DestroyImmediate(old);

            foreach (var point in pickup.GrabPoints)
            {
                var stockPosition = (Vector3)Field(typeof(GrabPoint), "position").GetValue(point);
                var chamber = chambers.OrderBy(m => Mathf.Abs(m.sharedMesh.bounds.center.y - stockPosition.y)).First().sharedMesh.bounds;
                Set(point, "position", chamber.center);
                Set(point, "offset", Mathf.Min(chamber.extents.x, chamber.extents.z));
            }

            var corkFilter = modelCork.GetComponentInChildren<MeshFilter>();
            Vector3 corkCenter = corkFilter.transform.TransformPoint(corkFilter.sharedMesh.bounds.center);
            foreach (var sphere in capColliders.OfType<SphereCollider>())
            {
                sphere.center = sphere.transform.InverseTransformPoint(corkCenter);
                sphere.radius = .025f;
            }
            foreach (var point in cap.GrabPoints)
                Set(point, "position", cap.transform.InverseTransformPoint(corkCenter));
        }

        static MeshCollider AddHull(Transform pickup, Collider donor, MeshFilter artPart)
        {
            var node = new GameObject("Crystal " + artPart.name + " collider");
            node.layer = donor.gameObject.layer;
            node.transform.SetParent(donor.transform.parent, false);
            node.transform.SetPositionAndRotation(pickup.position, pickup.rotation);
            var hull = node.AddComponent<MeshCollider>();
            hull.sharedMaterial = donor.sharedMaterial;
            hull.convex = true;
            hull.isTrigger = donor.isTrigger;
            hull.sharedMesh = artPart.sharedMesh;
            hull.enabled = donor.enabled;
            return hull;
        }

        static void EnableGemCrafting(NetworkPrefab gem, GenericCraftingComponent donor)
        {
            var craft=gem.GetComponent<GenericCraftingComponent>();
            if(craft!=null) return;
            craft=gem.gameObject.AddComponent<GenericCraftingComponent>();
            var pickup=gem.GetComponent<Pickup>();
            Set(craft,"pickup",pickup);
            Set(craft,"cachedTransform",gem.transform);
            Set(craft,"centrePointOffset",Vector3.zero);
            Set(craft,"material",gem.GetComponent<PhysicalMaterialPart>()?.PhysicalMaterial ?? donor.Material);
            Set(craft,"affectedByBehaviours",new MonoBehaviour[0]);
            Set(craft,"onAddedToProjectEffect",Field(typeof(GenericCraftingComponent),"onAddedToProjectEffect").GetValue(donor));
            ((ISerializationCallbackReceiver)craft).OnAfterDeserialize();
            Bind(gem.gameObject);
        }

        RegisteredWorkbenchRecipe RegisterRecipe(NetworkPrefab result, Glyph glyph, Item gem)
        {
            var recipeArt = art.transform.Find("Recipe");
            var stages = new WorkbenchStage[4];
            for (int i = 0; i < stages.Length; i++)
            {
                var node = recipeArt.Find(i == 0 ? "Frame" : "Gem " + (i - 1));
                var mesh = node.GetComponent<MeshFilter>().sharedMesh;
                stages[i] = new WorkbenchStage
                {
                    IngredientPrefabHash = i == 0 ? Buckle : BlueGem,
                    Level = i == 0 ? 0 : 1,
                    Mesh = mesh,
                    CollisionMesh = BoxHull(mesh.bounds),
                    Pose = new SerializedTransform { position = Vector3.up * .03f, rotation = Quaternion.identity, scale = Vector3.one }
                };
            }
            var definition = new WorkbenchRecipeDefinition
            {
                Key = "crystal-gourd-v1",
                PagePrefabName = PageName,
                Result = result,
                ResultGlyph = glyph,
                Stages = stages,
                SpawnOffset = new SerializedTransform { position = Vector3.up * .08f, rotation = Quaternion.identity, scale = Vector3.one }
            };
            // Gems are native pickups, but SyncLib only indexes ingredients found in stock recipes.
            var templatesField = Field(typeof(WorkbenchRecipeRegistry), "nativeTemplates");
            var templates = (GenericCraftingPieceDefinition[])templatesField.GetValue(null);
            var buckle = templates.First(template => template.source.Prefab.Hash == Buckle);
            var gemTemplate = new GenericCraftingPieceDefinition
            {
                source = gem,
                defaultMaterial = recipeArt.Find("Gem 0").GetComponent<MeshRenderer>().sharedMaterial,
                avoidsAtlasing = true,
                nailSettings = buckle.nailSettings,
                minChiselHitDamageToFall = buckle.minChiselHitDamageToFall
            };
            templatesField.SetValue(null, templates.Concat(new[]{gemTemplate}).ToArray());
            try
            {
                var registered = WorkbenchRecipeRegistry.Register(this, definition);
                if (!Application.isBatchMode) WorkbenchFinish.Register(registered.Recipe, art);
                return registered;
            }
            finally { templatesField.SetValue(null, templates); }
        }

        static Mesh BoxHull(Bounds b)
        {
            var mesh=new Mesh{name="Crystal Gourd chamber collision"};
            mesh.vertices=new[]{new Vector3(b.min.x,b.min.y,b.min.z),new Vector3(b.max.x,b.min.y,b.min.z),new Vector3(b.max.x,b.max.y,b.min.z),new Vector3(b.min.x,b.max.y,b.min.z),
                new Vector3(b.min.x,b.min.y,b.max.z),new Vector3(b.max.x,b.min.y,b.max.z),new Vector3(b.max.x,b.max.y,b.max.z),new Vector3(b.min.x,b.max.y,b.max.z)};
            mesh.triangles=new[]{0,2,1,0,3,2,4,5,6,4,6,7,0,4,7,0,7,3,1,2,6,1,6,5,3,7,6,3,6,2,0,1,5,0,5,4};
            mesh.RecalculateBounds();return mesh;
        }

        static Glyph MakeGlyph()
        {
            var glyph=ScriptableObject.CreateInstance<Glyph>();
            glyph.name="Crystal Gourd";
            uint hash = 58754;
            while (Values<Glyph>().ContainsKey(hash)) hash = hash == ushort.MaxValue ? 1u : hash + 1;
            Set(glyph,"hash",(int)hash);
            if (!Application.isBatchMode) Set(glyph,"sprite",art.transform.Find("Recipe icon").GetComponent<SpriteRenderer>().sprite);
            AddValue(glyph);return glyph;
        }

        static Item RegisterItem(NetworkPrefab stock,NetworkPrefab result,Glyph glyph)
        {
            var item=Object.Instantiate(stock.GetComponent<Pickup>().Item);
            item.name="Crystal Gourd";item.Prefab=result;
            Set(item,"hash",(int)result.Hash);Set(item,"glyph",glyph);
            result.GetComponent<Pickup>().Item=item;
            AddValue(item);
            return item;
        }

        static void RegisterPotionHoops(Item item)
        {
            var attachment = PrefabManager.GetPrefab(31418);
            if (attachment == null) throw new InvalidOperationException("Native Potion Hoops prefab is unavailable");
            var docks = attachment.GetComponentsInChildren<AdditionalOwnedDock>(true)
                .Where(dock => dock.Settings != null && dock.Settings.name == "Potion Hoops").ToArray();
            if (docks.Length == 0) throw new InvalidOperationException("Native Potion Hoops docks are unavailable");
            var neck = art.transform.Find("Neck Direction").localPosition;
            var mouth = art.transform.Find("Mouth").localPosition;
            var rotation = Quaternion.FromToRotation(mouth - neck, Vector3.up);
            foreach (var settings in docks.Select(dock => dock.Settings).Distinct())
            {
                var hoop = new Item.CustomDockPositioning(settings, PotionHoopTarget.Seat - rotation * neck, rotation.eulerAngles);
                foreach (var fieldName in new[] { "customPositions", "customPositionsWhenCrafted" })
                {
                    var field = Field(typeof(Item), fieldName);
                    var positions = (Item.CustomDockPositioning[])field.GetValue(item);
                    field.SetValue(item, new[] { hoop }.Concat(positions.Where(position => position.Type != settings)).ToArray());
                }
                if (!settings.IncludedItems.Contains(item)) settings.IncludedItems.Add(item);
            }
            if (!Application.isBatchMode)
            {
                PotionHoopTarget.Configure(item, neck);
                foreach (var dock in docks)
                    dock.gameObject.AddComponent<PotionHoopTarget>();
            }
        }

        static void PrepareRigidbody(GameObject root)
        {
            var old=root.GetComponent<NetworkRigidbody>();
            if(old!=null) Object.DestroyImmediate(old); // SyncLib adds the replacement at registration.
        }

        static void Bind(GameObject root)
        {
            foreach(var pickup in root.GetComponentsInChildren<Pickup>(true))
            {
                Set(pickup,"rigidbody",pickup.GetComponent<Rigidbody>());
                Set(pickup,"networkRigidbody",pickup.GetComponent<NetworkRigidbody>());
            }
            foreach(var body in root.GetComponentsInChildren<NetworkRigidbody>(true))
            {
                Set(body,"cachedTransform",body.transform);
                body.UpdateRigidbody();
            }
            foreach(var entity in root.GetComponentsInChildren<NetworkEntity>(true))
            {
                var behaviours=entity.GetComponents<MonoBehaviour>().Where(b=>b is INetworkEntityBehaviour).ToArray();
                foreach(var behaviour in behaviours.OfType<NetworkEntityBehaviour>()) Set(behaviour,"entity",entity);
                Set(entity,"networkEntityBehaviours",behaviours);Set(entity,"hasNetworkBehaviours",behaviours.Length>0);
                Set(entity,"prefab",entity.GetComponent<NetworkPrefab>());
                Set(entity,"commonPickup",entity.GetComponent<Pickup>());Set(entity,"commonInteractable",entity.GetComponent<Interactable>());
            }
        }

        internal static FieldInfo Field(Type type,string name) => AccessTools.Field(type,name) ?? throw new MissingFieldException(type.FullName,name);
        internal static void Set(object target,string field,object value) => Field(target.GetType(),field).SetValue(target,value);
        static Dictionary<uint,T> Values<T>() where T:HashedGeneralValue<T>
        {
            HashedGeneralValue<T>.CheckItems();
            return (Dictionary<uint,T>)Field(typeof(HashedGeneralValue<T>),"items").GetValue(null);
        }
        static void AddValue<T>(T value) where T:HashedGeneralValue<T>
        {
            Values<T>().Add(value.Hash,value);
            Field(typeof(HashedGeneralValue<T>),"itemsByName").SetValue(null,null);
        }
    }

    internal sealed class PotionHoopTarget : MonoBehaviour
    {
        // The stock flask's neck sits above the hidden snap pivot.
        internal static readonly Vector3 Seat = Vector3.up * .184f;
        const float SnapRadius = .075f;
        static readonly HashSet<PotionHoopTarget> activeTargets = new HashSet<PotionHoopTarget>();
        static Item gourdItem;
        static Vector3 gourdNeck;
        PickupDock dock;

        internal static void Configure(Item item, Vector3 neck)
        {
            gourdItem = item;
            gourdNeck = neck;
        }

        void Awake() => dock = GetComponent<PickupDock>();
        void OnEnable() => activeTargets.Add(this);
        void OnDisable() => activeTargets.Remove(this);

        internal static void FindByNeck(Pickup ___pickup, ref PickupDock closest, ref Interactor interactor, ref int __result)
        {
            if (__result != 0 || gourdItem == null || ___pickup.Item != gourdItem || ___pickup.Interactors.Count == 0)
                return;

            Vector3 neck = ___pickup.transform.TransformPoint(gourdNeck);
            float bestDistance = SnapRadius * SnapRadius;
            foreach (var target in activeTargets)
            {
                var candidate = target.dock;
                if (candidate == null || !candidate.PickupRange.enabled || candidate.Entity.Scene == null || candidate.HasDockedPickup)
                    continue;
                float distance = (candidate.SnapPoint.TransformPoint(Seat) - neck).sqrMagnitude;
                if (distance >= bestDistance)
                    continue;
                var owner = candidate.PlayerController;
                if (candidate.IsOwnedByPlayer && (owner == null || !owner.IsAlive || owner.NetworkPlayer.StateManager.IsLeavingCurrentState))
                    continue;

                foreach (var hand in ___pickup.Interactors)
                {
                    if (candidate.CheckAllowed(hand, ___pickup) != PickupDock.AllowedResult.Ok)
                        continue;
                    closest = candidate;
                    interactor = hand;
                    bestDistance = distance;
                    break;
                }
            }
            if (closest != null)
                __result = 1;
        }
    }

    internal sealed class CrystalCork : MonoBehaviour
    {
        [SerializeField] LiquidContainer container;
        [SerializeField] LiquidContainerLid lid;
        [SerializeField] GameObject model;

        internal void Configure(LiquidContainer liquid, LiquidContainerLid nativeLid, GameObject cork)
        {
            container = liquid;
            lid = nativeLid;
            model = cork;
            model.SetActive(false);
        }

        void Awake() => container.ContentLevelChanged += OnContentChanged;
        void OnDestroy()
        {
            if (container != null) container.ContentLevelChanged -= OnContentChanged;
        }

        void OnContentChanged(ILiquidContainer liquid, int oldLevel, int newLevel)
        {
            if (NetworkSceneManager.IsServer && newLevel == 0 && lid.IsOnContainer)
                lid.RemoveLid(isForced: false, isPlayingEffects: false);
        }

        internal static void UpdateVisibility(LiquidContainerLid __instance)
        {
            var cork = __instance.GetComponent<CrystalCork>();
            if (cork != null) cork.model.SetActive(__instance.IsOnContainer);
        }
    }

    internal static class WorkbenchFinish
    {
        static readonly Dictionary<GenericCraftingPieceDefinition, MeshRenderer> pieces =
            new Dictionary<GenericCraftingPieceDefinition, MeshRenderer>();
        static readonly FieldInfo definition = Mod.Field(typeof(GenericCraftingPiece), "definition");
        static readonly FieldInfo visual = Mod.Field(typeof(GenericCraftingPiece), "mainMeshRenderer");
        static readonly FieldInfo filters = Mod.Field(typeof(GenericCraftingPiece), "meshFilters");

        internal static void Register(GenericCraftingRecipe recipe, GameObject art)
        {
            var body = art.transform.Find("Body/LOD0");
            for (int i = 0; i < recipe.Pieces.Length; i++)
            {
                var part = body.Find(i == 0 ? "Steel frame" : "Chamber " + (i - 1));
                pieces.Add(recipe.Pieces[i], part.GetComponent<MeshRenderer>());
            }
        }

        internal static void Apply(GenericCraftingPiece __instance)
        {
            if (!__instance.IsFilled || !(definition.GetValue(__instance) is GenericCraftingPieceDefinition piece) ||
                !pieces.TryGetValue(piece, out var artPart)) return;
            var renderer = (MeshRenderer)visual.GetValue(__instance);
            var mesh = artPart.GetComponent<MeshFilter>().sharedMesh;
            foreach (var filter in (MeshFilter[])filters.GetValue(__instance))
                filter.sharedMesh = mesh;
            renderer.sharedMaterial = artPart.sharedMaterial;
        }

        internal static void Clear() => pieces.Clear();
    }

    internal static class StereoMaterials
    {
        const string Keyword = "CRYSTAL_GOURD_STEREO";
        static Material[] materials;
        static readonly Stack<bool> cameras = new Stack<bool>();
        static bool active;

        internal static void Install(GameObject art)
        {
            materials = art.GetComponentsInChildren<MeshRenderer>(true).SelectMany(r => r.sharedMaterials).Distinct().ToArray();
            Camera.onPreCull += Before;
            Camera.onPostRender += After;
        }

        internal static void Dispose()
        {
            Camera.onPreCull -= Before;
            Camera.onPostRender -= After;
            Set(false);
            materials = null;
            cameras.Clear();
        }

        static void Before(Camera camera)
        {
            cameras.Push(active);
            Set(camera.stereoEnabled && XRSettings.stereoRenderingMode == XRSettings.StereoRenderingMode.SinglePassInstanced);
        }

        static void After(Camera camera) => Set(cameras.Count != 0 && cameras.Pop());

        static void Set(bool value)
        {
            if (value == active) return;
            active = value;
            foreach (var material in materials)
            {
                if (value) material.EnableKeyword(Keyword);
                else material.DisableKeyword(Keyword);
            }
        }
    }
}
