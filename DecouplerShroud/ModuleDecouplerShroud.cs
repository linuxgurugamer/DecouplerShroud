using KSP.Localization;
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UniversalStorage2;

namespace DecouplerShroud
{
    public class ModuleDecouplerShroud : PartModule, IAirstreamShield
    {

        float[] snapSizes = new float[] { .63f, 1.25f, 2.5f, 3.75f, 5f, 7.5f };
        int[] segmentCountLUT = new int[] { 1, 2, 3, 4, 6 };
        int[] collPerSegmentLUT = new int[] { 12, 6, 4, 3, 2 };
        public static string DECOUPLERSHROUD_GO_NAME = Localizer.Format("#LOC_DecouplerShroud_2");

        [KSPField(isPersistant = true)]
        public int nSides = 24;

        [KSPField(guiName = "DecouplerShroud", isPersistant = true, guiActiveEditor = true, guiActive = false), UI_Toggle(invertButton = true)]
        public bool shroudEnabled = false;

        [KSPField(guiName = "Automatic Shroud Size", isPersistant = true, guiActiveEditor = true, guiActive = false), UI_Toggle(invertButton = true)]
        public bool autoDetectSize = true;

        [KSPField(guiName = "Top", isPersistant = true, guiActiveEditor = true, guiActive = false)]
        [UI_FloatEdit(scene = UI_Scene.Editor, minValue = .01f, maxValue = Mathf.Infinity, incrementLarge = .625f, incrementSlide = 0.01f, incrementSmall = 0.05f, unit = "m", sigFigs = 2, useSI = false)]
        public float topWidth = 1.25f;

        [KSPField(guiName = "Bottom", isPersistant = true, guiActiveEditor = true, guiActive = false)]
        [UI_FloatEdit(scene = UI_Scene.Editor, minValue = .01f, maxValue = Mathf.Infinity, incrementLarge = .625f, incrementSlide = 0.01f, incrementSmall = 0.05f, unit = "m", sigFigs = 2, useSI = false)]
        public float botWidth = 1.25f;

        [KSPField(guiName = "Thickness", isPersistant = true, guiActiveEditor = true, guiActive = false)]
        [UI_FloatEdit(scene = UI_Scene.Editor, minValue = .01f, maxValue = 1f, incrementLarge = .1f, incrementSlide = 0.01f, incrementSmall = 0.01f, sigFigs = 2, useSI = false)]
        public float thickness = .1f;

        [KSPField(guiName = "Height", isPersistant = true, guiActiveEditor = true, guiActive = false)]
        [UI_FloatEdit(scene = UI_Scene.Editor, minValue = .01f, maxValue = Mathf.Infinity, incrementLarge = 0.25f, incrementSlide = 0.01f, incrementSmall = 0.02f, unit = "m", sigFigs = 2, useSI = false)]
        public float height = 1.25f;

        [KSPField(guiName = "Vertical Offset", isPersistant = true, guiActiveEditor = true, guiActive = false)]
        [UI_FloatEdit(scene = UI_Scene.Editor, minValue = -Mathf.Infinity, maxValue = Mathf.Infinity, incrementLarge = .1f, incrementSlide = 0.01f, incrementSmall = 0.01f, unit = "m", sigFigs = 2, useSI = false)]
        public float vertOffset = 0.0f;

        [KSPField(guiName = "Jettison Mode", isPersistant = true, guiActiveEditor = true, guiActive = false)]
        [UI_ChooseOption(affectSymCounterparts = UI_Scene.Editor, options = new[] { "Stay", "2 Shells", "3 Shells", "4 Shells", "6 Shells" }, scene = UI_Scene.Editor, suppressEditorShipModified = true)]
        int segmentIndex;

        [KSPField(guiName = "Shroud Texture", isPersistant = false, guiActiveEditor = true, guiActive = false)]
        [UI_ChooseOption(affectSymCounterparts = UI_Scene.Editor, options = new[] { "None" }, scene = UI_Scene.Editor, suppressEditorShipModified = true)]
        public int textureIndex;


        [KSPField(isPersistant = true)]
        public string textureName;

        //True when the shroud uses the TURD recolour variant of the selected texture
        [KSPField(isPersistant = true)]
        public bool turdRecolor = false;

        //Pure single-colour textures: repainting them looks the same as the
        //plain version, so they get no recolour entry in the texture dropdown
        static readonly string[] NoRecolourVariant = { "Dark", "Metallic" };

        //GameDatabase url of the player's flag shown on the outside of the shroud.
        //Empty means "no flag", in that case the regular texture is used.
        [KSPField(isPersistant = true)]
        public string shroudFlagURL = "";

        [KSPField(isPersistant = false)]
        public float defaultBotWidth = 0;
        [KSPField(isPersistant = false)]
        public float defaultVertOffset = 0;
        [KSPField(isPersistant = false)]
        public float defaultThickness = 0.1f;
        [KSPField(isPersistant = false)]
        public float radialSnapMargin = .15f;
        [KSPField(isPersistant = false)]
        public float bottomEdgeSize = .1f;
        [KSPField(isPersistant = false)]
        public float topBevelSize = .05f;
        [KSPField(isPersistant = false)]
        public float antiZFightSizeIncrease = .001f;
        [KSPField(isPersistant = false)]
        public int outerEdgeLoops = 13;
        [KSPField(isPersistant = false)]
        public int topEdgeLoops = 7;
        [KSPField(isPersistant = false)]
        public float jettisonVelocity = 1;
        [KSPField(isPersistant = false)]
        public bool collisionEnabled;
        [KSPField(isPersistant = false)]
        public float editorMinAlpha = .2f;

        [KSPField(isPersistant = true)]
        public int segments = 1;
        [KSPField(isPersistant = true)]
        public int collPerSegment = 1;

        bool setupFinished = false;
        ModuleJettison[] engineShrouds;
        GameObject shroudGO;
        Material[] shroudMats;
        ShroudShaper shroudShaper;

        [KSPField(isPersistant = true)]
        public bool jettisoned = false;
        [KSPField]
        bool turnedOffEngineShroud;

        //Needed to call updateTexture a few times after changing segment count
        //otherwise the transparency of the outside shroud is constant for some reason
        int Fix_SegmentChangedCallUpdateTexture = 0;

        // TURD / TexturesUnlimited integration
        // Two KSPTextureSwitch sections live on the part: "Shroud" is the colour
        // data holder for the procedural shroud (painted by this module), "Ring"
        // is a normal TURD recolour of the static ring mesh handled by TU itself.
        const string TU_SHROUD_SECTION = "Shroud";
        const string TU_RING_SECTION = "Ring";

        //Fallback colour source when the part carries no KSPTextureSwitch
        const string DS_SHROUD_TEXTURE_SET = "DS_Shroud_Paint";

        PartModule shroudTUSwitch;
        PartModule ringTUSwitch;
        PartModule tuRecolorGUI;
        static bool tuAssemblyChecked = false;
        static bool tuInstalled = false;
        Color cachedMainColor = Color.white;
        Color cachedSecondColor = Color.white;
        Color cachedDetailColor = Color.white;
        bool cachedTUColorsValid = false;
        float nextTUPollTime = 0f;

        //true when decoupler has no grandparent in the editor and automatic size detection is active
        [KSPField(isPersistant = true)]
        public bool invisibleShroud;

        //Variables for detecting wheter automatic size needs to be recalculated
        Vector3 lastPos;
        Vector3 lastScale;
        Vector3 lastBounds;
        Vector3 lastShroudAttachedPos;
        Vector3 lastShroudAttachedScale;
        Vector3 lastShroudAttachedBounds;
        Part lastShroudAttachedPart;
        Part lastShroudedPart = null;

        public void setup()
        {
            //Debug.Log("[Decoupler Shroud] jet: " + jettisoned + ", attached: " + part.isAttached + ", inv: "+invisibleShroud);

            //Get rid of decoupler shroud module if no top node found
            if (destroyShroudIfNoTopNode())
            {
                return;
            }

            segmentIndex = Mathf.Clamp(segmentIndex, 0, segmentCountLUT.Length - 1);
            segments = segmentCountLUT[segmentIndex];
            collPerSegment = collPerSegmentLUT[segmentIndex];
            //Debug.Log("!!set segment count to: " + segments + ", Index: " + segmentIndex);

            getTextureNames();
            //Remove copied decoupler shroud when copied
            Transform copiedDecouplerShroud = part.FindModelComponent<Transform>().Find(DECOUPLERSHROUD_GO_NAME);
            if (copiedDecouplerShroud != null)
            {
                Destroy(copiedDecouplerShroud.gameObject);
                shroudShaper = null;
            }

            // Set up localization
            DECOUPLERSHROUD_GO_NAME = Localizer.Format("#LOC_DecouplerShroud_2");

            Fields[nameof(shroudEnabled)].guiName = Localizer.Format("#LOC_DecouplerShroud_2");
            Fields[nameof(autoDetectSize)].guiName = Localizer.Format("#LOC_DecouplerShroud_10");

            Fields[nameof(topWidth)].guiName = Localizer.Format("#LOC_DecouplerShroud_11");
            Fields[nameof(botWidth)].guiName = Localizer.Format("#LOC_DecouplerShroud_12");
            Fields[nameof(thickness)].guiName = Localizer.Format("#LOC_DecouplerShroud_13");
            Fields[nameof(height)].guiName = Localizer.Format("#LOC_DecouplerShroud_14");
            Fields[nameof(vertOffset)].guiName = Localizer.Format("#LOC_DecouplerShroud_15");
            Fields[nameof(segmentIndex)].guiName = Localizer.Format("#LOC_DecouplerShroud_16");
            Fields[nameof(textureIndex)].guiName = Localizer.Format("#LOC_DecouplerShroud_17");

            UpdateFlagButtonName();



            //Set up events
            part.OnEditorAttach += partReattached;
            part.OnEditorDetach += partDetached;

            Fields[nameof(shroudEnabled)].OnValueModified += activeToggled;
            Fields[nameof(autoDetectSize)].OnValueModified += setButtonActive;
            Fields[nameof(autoDetectSize)].OnValueModified += detectSize;

            Fields[nameof(topWidth)].OnValueModified += updateShroud;
            Fields[nameof(botWidth)].OnValueModified += updateShroud;
            Fields[nameof(height)].OnValueModified += updateShroud;
            Fields[nameof(thickness)].OnValueModified += updateShroud;
            Fields[nameof(vertOffset)].OnValueModified += updateShroud;
            Fields[nameof(textureIndex)].OnValueModified += changeMaterial;

            Fields[nameof(segmentIndex)].OnValueModified += segmentUpdate;

            setButtonActive();

            if (HighLogic.LoadedSceneIsFlight)
            {
                if (GetShroudedPart() != null && shroudEnabled)
                {
                    GetShroudedPart().AddShield(this);
                }
            }
            createNewShroudGO();
            //detectSize();
            setupFinished = true;
        }

        public void Start()
        {
            setup();
        }

        void Update()
        {
            if (!setupFinished)
            {
                return;
            }

            if (HighLogic.LoadedSceneIsEditor)
            {
                if (part.isAttached && shroudEnabled)
                {
                    detectRequiredRecalculation();
                    // Throttle TURD polling to avoid per-frame reflection + material churn
                    if (Time.realtimeSinceStartup >= nextTUPollTime)
                    {
                        nextTUPollTime = Time.realtimeSinceStartup + 0.25f;
                        PollTURDState();
                    }
                    UpdateMaterialsOpacity();

                }
            }

            if (lastShroudedPart != GetShroudedPart())
            {
                onShroudedPartChanged();
                lastShroudedPart = GetShroudedPart();
            }

        }


        void onShroudedPartChanged()
        {
            if (HighLogic.LoadedSceneIsFlight)
            {
                if (lastShroudedPart != null)
                {
                    //Debug.Log("shrouded Part Changed! was " + lastShroudedPart + " is " + GetShroudedPart());
                    lastShroudedPart.RemoveShield(this);
                    if (!jettisoned)
                    {
                        if (GetComponent<ModuleDecouple>() != null && part.GetComponent<ModuleDecouple>().isDecoupled)
                        {
                            Jettison();
                        }
                        else
                        {
                            // The call to check for the DLL has to be OUTSIDE the method being called
                            if (SpaceTuxUtility.HasMod.hasMod(Localizer.Format("#LOC_DecouplerShroud_1")))
                                US2Jettison();
                        }
                    }
                }
            }
            if (HighLogic.LoadedSceneIsEditor)
            {
                // Toggle engineShroud if new engine is placed on decoupler
                detectSize();
                setEngineShroudActivity();
            }
        }

        void US2Jettison()
        {
            if (GetComponent<USDecouple>() != null && part.GetComponent<USDecouple>().isDecoupled)
            {
                Jettison();
            }
        }

        void detectRequiredRecalculation()
        {
            bool requiredRecalc = false;

            //Check if collider bounds changed (for example procedural parts can cause this)
            if (part.collider != null)
            {
                if (lastBounds != part.collider.bounds.size)
                {
                    lastBounds = part.collider.bounds.size;
                    requiredRecalc = true;
                }
            }

            //Checking if part position/scale changed
            if (transform.position != lastPos || transform.localScale != lastScale)
            {
                lastPos = transform.position;
                lastScale = transform.localScale;
                requiredRecalc = true;
            }
            //If there is a new attached part
            if (GetShroudAttachedPart() != lastShroudAttachedPart)
            {
                lastShroudAttachedPart = GetShroudAttachedPart();
                requiredRecalc = true;
            }
            //Check if attached part changed
            if (lastShroudAttachedPart != null)
            {
                if (lastShroudAttachedPos != lastShroudAttachedPart.transform.position
                || lastShroudAttachedScale != lastShroudAttachedPart.transform.localScale)
                {
                    lastShroudAttachedPos = lastShroudAttachedPart.transform.position;
                    lastShroudAttachedScale = lastShroudAttachedPart.transform.localScale;
                    requiredRecalc = true;
                }
                //Check if collider bounds of attatched part changed (for example procedural parts can cause this)
                if (lastShroudAttachedPart.collider != null)
                {
                    if (lastShroudAttachedBounds != lastShroudAttachedPart.collider.bounds.size)
                    {
                        lastShroudAttachedBounds = lastShroudAttachedPart.collider.bounds.size;
                        requiredRecalc = true;
                    }
                }
            }

            if (requiredRecalc)
            {
                detectSize();
            }

        }

        //Jettisons the shroud
        [KSPEvent(guiName = "Jettison", guiActive = true, guiActiveEditor = false)]
        public void Jettison()
        {
            //Debug.Log("Jettison called on DecouplerShroud of "+part.name);

            if (segments < 2 || !HighLogic.LoadedSceneIsFlight)
            {
                return;
            }
            Events[nameof(Jettison)].guiActive = false;

            jettisoned = true;

            for (int i = 0; i < shroudGO.transform.childCount; i++)
            {
                GameObject c = shroudGO.transform.GetChild(i).gameObject;
                //c.layer = 19;
                physicalObject ph = c.AddComponent<physicalObject>();
                ph.rb = c.AddComponent<Rigidbody>();

                float ang = 2 * Mathf.PI * (i + .5f) / (float)segments;
                ph.rb.AddRelativeForce(new Vector3(Mathf.Cos(ang), 0, Mathf.Sin(ang)) * jettisonVelocity, ForceMode.VelocityChange);
            }
            shroudGO.transform.DetachChildren();
        }

        //Gets textures from Textures folder and loads them into surfaceTextures list + set Field options
        void getTextureNames()
        {
            if (ShroudTexture.shroudTextures == null)
            {
                ShroudTexture.LoadTextures();
            }

            int count = ShroudTexture.shroudTextures.Count;
            if (count == 0)
            {
                return;
            }

            //With TURD installed the field lists every texture first and then
            //each texture's recolour variant, so the recolour switch lives here
            bool tu = IsTUInstalled();

            string recolour = Localizer.Format("#LOC_DecouplerShroud_18");
            //Pure single-colour textures (Dark, Metallic, ...) get no recolour
            //variant: changing their colours cannot produce a different look
            List<int> recolourTargets = new List<int>();
            for (int i = 0; i < count; i++)
            {
                if (tu && HasRecolourVariant(ShroudTexture.shroudTextures[i].name))
                {
                    recolourTargets.Add(i);
                }
            }

            int total = count + recolourTargets.Count;
            string[] options = new string[total];
            int baseIndex = 0;
            int recolourIndex = -1;

            for (int i = 0; i < count; i++)
            {
                string name = ShroudTexture.shroudTextures[i].displayName;

                options[i] = name;

                int pos = recolourTargets.IndexOf(i);
                if (pos >= 0)
                {
                    //Same wording as the TURD stock patches: "<texture> recolor"
                    options[count + pos] = name + " " + recolour;
                }

                //Sets textureindex to the saved texture
                if (ShroudTexture.shroudTextures[i].name.Equals(textureName))
                {
                    baseIndex = i;
                    recolourIndex = pos;
                }
            }

            textureIndex = tu && turdRecolor && recolourIndex >= 0
                ? Mathf.Clamp(count + recolourIndex, 0, total - 1)
                : Mathf.Clamp(baseIndex, 0, total - 1);

            BaseField textureField = Fields[nameof(textureIndex)];
            UI_ChooseOption textureOptions = (UI_ChooseOption)textureField.uiControlEditor;
            textureOptions.options = options;
        }

        //Executes when shroud is enabled/disabled
        void activeToggled(object arg)
        {
            setEngineShroudActivity();
            setButtonActive();
            detectSize();
            updateShroud();
            UpdateFlagButtonName();
        }

        //Executes when amount of segments is changed
        private void segmentUpdate(object arg1)
        {
            segments = segmentCountLUT[segmentIndex];
            collPerSegment = collPerSegmentLUT[segmentIndex];
            //Trigger rebuilding of shrouds
            createNewShroudGO();

        }

        //Disables and reenables stock engine shrouds
        void setEngineShroudActivity()
        {
            if (!shroudEnabled)
                return;

            Part topPart = GetShroudedPart();

            if (topPart != null)
            {
                engineShrouds = topPart.GetComponents<ModuleJettison>();
                if (engineShrouds.Length > 0)
                {
                    turnedOffEngineShroud = engineShrouds[0].shroudHideOverride;
                    foreach (ModuleJettison engineShroud in engineShrouds)
                    {
                        engineShroud.shroudHideOverride = true;
                    }
                }
            }
        }

        //Enables or disables KSPFields based on values
        void setButtonActive(object arg) { setButtonActive(); }
        void setButtonActive()
        {

            if (!HighLogic.LoadedSceneIsEditor)
                return;

            if (shroudEnabled)
            {
                Fields[nameof(autoDetectSize)].guiActiveEditor = true;
                Fields[nameof(segmentIndex)].guiActiveEditor = true;
                Fields[nameof(textureIndex)].guiActiveEditor = true;

            }
            else
            {
                Fields[nameof(autoDetectSize)].guiActiveEditor = false;
                Fields[nameof(segmentIndex)].guiActiveEditor = false;
                Fields[nameof(textureIndex)].guiActiveEditor = false;

            }
            if (shroudEnabled && !autoDetectSize)
            {
                Fields[nameof(topWidth)].guiActiveEditor = true;
                Fields[nameof(botWidth)].guiActiveEditor = true;
                Fields[nameof(height)].guiActiveEditor = true;
                Fields[nameof(vertOffset)].guiActiveEditor = true;
                Fields[nameof(thickness)].guiActiveEditor = true;
            }
            else
            {
                Fields[nameof(topWidth)].guiActiveEditor = false;
                Fields[nameof(botWidth)].guiActiveEditor = false;
                Fields[nameof(height)].guiActiveEditor = false;
                Fields[nameof(vertOffset)].guiActiveEditor = false;
                Fields[nameof(thickness)].guiActiveEditor = false;
            }

            Events[nameof(Jettison)].guiActive = !jettisoned && shroudEnabled && (segments > 1);
            UpdateTUModuleVisibility();
            //Debug.Log("set jettison gui to: "+ (!jettisoned && shroudEnabled && (segments > 1)) +", "+jettisoned+", "+shroudEnabled+", "+(segments>1)+", "+segments);
        }

        void changeMaterial(object arg) { changeMaterial(); }
        void changeMaterial()
        {
            //The texture field holds every texture followed by every recolour
            //variant, so anything past the plain textures is a recolour variant
            int texCount = ShroudTexture.shroudTextures != null ? ShroudTexture.shroudTextures.Count : 0;
            turdRecolor = IsTUInstalled() && texCount > 0 && textureIndex >= texCount;

            ShroudTexture shroudTex = ShroudTexture.shroudTextures[GetBaseTextureIndex()];

            //save current textures name
            textureName = shroudTex.name;
            CreateMaterials(shroudTex);

            UpdateTUModuleVisibility();
            //The flag button only exists for the LogoHawk7 texture, so its
            //visibility has to follow the texture switch
            UpdateFlagButtonName();
            updateTextureScale();
        }

        //Maps the shroud texture dropdown index back to the index in ShroudTexture.shroudTextures.
        //Recolour entries sit behind the plain list, but only recolourable
        //textures occupy a slot there, so the mapping is sparse.
        int GetBaseTextureIndex()
        {
            var list = ShroudTexture.shroudTextures;
            int count = list != null ? list.Count : 0;
            if (count == 0)
            {
                return 0;
            }
            if (textureIndex < count)
            {
                return Mathf.Clamp(textureIndex, 0, count - 1);
            }
            int n = textureIndex - count;
            for (int i = 0; i < count; i++)
            {
                if (!HasRecolourVariant(list[i].name))
                {
                    continue;
                }
                if (n == 0)
                {
                    return i;
                }
                n--;
            }
            return count - 1;
        }

        static bool HasRecolourVariant(string textureName)
        {
            return Array.IndexOf(NoRecolourVariant, textureName) < 0;
        }

        static bool IsTUInstalled()
        {
            if (!tuAssemblyChecked)
            {
                tuAssemblyChecked = true;
                foreach (AssemblyLoader.LoadedAssembly a in AssemblyLoader.loadedAssemblies)
                {
                    if (a.name == "TexturesUnlimited")
                    {
                        tuInstalled = true;
                        break;
                    }
                }
            }
            return tuInstalled;
        }

        void updateTextureScale()
        {
            if (shroudMats == null)
            {
                changeMaterial();
                return;
            }
            if (shroudMats[0] == null)
            {
                Debug.LogWarning("called updateTextureScale while shroudMats[0] == null");
                changeMaterial();
                return;
            }

            ShroudTexture shroudTex = ShroudTexture.shroudTextures[GetBaseTextureIndex()];

            Vector2 sideSize = new Vector2(Mathf.Max(botWidth, topWidth), new Vector2(height, topWidth - botWidth).magnitude);
            Vector2 topSize = new Vector2(topWidth, topWidth * thickness);

            shroudTex.textures[0].SetTextureScale(shroudMats[0], sideSize);
            shroudTex.textures[1].SetTextureScale(shroudMats[1], topSize);
            shroudTex.textures[2].SetTextureScale(shroudMats[2], sideSize);

            //Has to come after the calls above, they would reset the flag's tiling
            ApplyFlagTexture();

            if (shroudGO == null)
            {
                return;
            }
            foreach (Renderer r in shroudGO.GetComponentsInChildren<Renderer>())
            {
                if (r.materials != shroudMats)
                {
                    foreach (Material mat in r.materials)
                    {
                        if (mat != null)
                            Destroy(mat);
                    }
                    r.materials = shroudMats;
                }
            }
        }

        //Creates the material for the mesh
        void CreateMaterials(ShroudTexture shroudTex)
        {
            //Clean up old materials
            if (shroudMats != null)
            {
                foreach (Material mat in shroudMats)
                {
                    if (mat != null)
                    {
                        Destroy(mat);
                    }
                }
            }
            shroudMats = new Material[3];

            bool useTURD = IsShroudTUPaintMode();

            for (int i = 0; i < shroudMats.Length; i++)
            {

                SurfaceTexture surf = shroudTex.textures[i];

                if (useTURD)
                {
                    surf.EnsureDefaultRecolorData(GetDefaultRecolorMaskColor(i));
                    shroudMats[i] = surf.CreateTURecolorMaterial();
                    if (shroudMats[i] == null)
                    {
                        shroudMats[i] = Instantiate(surf.mat);
                    }
                }
                else
                {
                    shroudMats[i] = Instantiate(surf.mat);
                }

                shroudMats[i].name = Localizer.Format("#LOC_DecouplerShroud_3") + i + ", " + segments + Localizer.Format("#LOC_DecouplerShroud_4");

                if (HighLogic.LoadedSceneIsEditor)
                {
                    // Enables transparency in editor
                    shroudMats[i].renderQueue = 3000;
                }
            }

            if (useTURD)
            {
                ApplyTUColorsToShroudMats();
            }

            ApplyFlagTexture();
        }

        bool IsShroudTUPaintMode()
        {
            if (!IsTUInstalled() || !turdRecolor)
            {
                return false;
            }
            //The KSPTextureSwitch section is only a colour source of truth. When
            //a part has no TURD patch the shroud still gets the TU material, it
            //just falls back to the texture set's default colours.
            InitTUModules();
            return true;
        }

        void InitTUModules()
        {
            if (shroudTUSwitch == null || ringTUSwitch == null)
            {
                foreach (PartModule m in part.Modules)
                {
                    if (m.moduleName != "KSPTextureSwitch")
                        continue;
                    try
                    {
                        BaseField sf = m.Fields["sectionName"];
                        string section = sf == null ? null : sf.GetValue<string>(m);
                        if (section == TU_SHROUD_SECTION && shroudTUSwitch == null)
                        {
                            shroudTUSwitch = m;
                        }
                        else if (section == TU_RING_SECTION && ringTUSwitch == null)
                        {
                            ringTUSwitch = m;
                        }
                    }
                    catch { }
                }
            }

            if (tuRecolorGUI == null)
            {
                foreach (PartModule m in part.Modules)
                {
                    if (m.moduleName == "SSTURecolorGUI")
                    {
                        tuRecolorGUI = m;
                        break;
                    }
                }
            }
        }

        string GetShroudTUSet()
        {
            if (shroudTUSwitch == null)
                return null;
            try
            {
                BaseField f = shroudTUSwitch.Fields["currentTextureSet"];
                return f.GetValue<string>(shroudTUSwitch);
            }
            catch
            {
                return null;
            }
        }

        Color GetDefaultRecolorMaskColor(int surfaceIndex)
        {
            switch (surfaceIndex)
            {
                case 0: return new Color(1f, 0f, 0f, 1f);   // outside -> primary (R)
                case 1: return new Color(0f, 1f, 0f, 1f);   // top -> secondary (G)
                default: return new Color(0f, 0f, 1f, 1f);  // inside -> detail (B)
            }
        }

        void UpdateTUModuleVisibility()
        {
            if (!IsTUInstalled())
            {
                return;
            }

            InitTUModules();

            //Both switches only ever hold one texture set, so there is nothing
            //to pick in them: the shroud variant is chosen in the shroud texture
            //dropdown and the ring is always recolourable. Hide them from the PAW.
            HideModuleFromPAW(shroudTUSwitch);
            HideModuleFromPAW(ringTUSwitch);

            //The recolouring GUI drives the ring row as well, so it stays
            //available whenever TURD is installed, not only in recolour mode.
            bool showRecolor = HighLogic.LoadedSceneIsEditor;
            if (tuRecolorGUI != null)
            {
                try
                {
                    foreach (BaseField f in tuRecolorGUI.Fields)
                    {
                        f.guiActiveEditor = showRecolor;
                        f.guiActive = false;
                    }
                    foreach (BaseEvent e in tuRecolorGUI.Events)
                    {
                        e.guiActiveEditor = showRecolor;
                        e.guiActive = false;
                    }
                }
                catch { }
            }
        }

        static void HideModuleFromPAW(PartModule module)
        {
            if (module == null)
            {
                return;
            }
            try
            {
                foreach (BaseField f in module.Fields)
                {
                    f.guiActiveEditor = false;
                    f.guiActive = false;
                }
                foreach (BaseEvent e in module.Events)
                {
                    e.guiActiveEditor = false;
                    e.guiActive = false;
                }
            }
            catch { }
        }

        struct TUChannelColor
        {
            public Color color;
            public float specular;
            public float metallic;
            public float detail;
        }

        // Reads the current recolour colours from the KSPTextureSwitch module via the
        // KSPShaderTools.IRecolorable interface (getSectionColors). This is the source of
        // truth TU uses to recolour meshes, so the procedural shroud matches the ring.
        bool TryGetTUChannelColors(out TUChannelColor[] channels)
        {
            channels = null;
            if (shroudTUSwitch == null)
                return false;

            try
            {
                MethodInfo getSectionColors = null;
                foreach (Type iface in shroudTUSwitch.GetType().GetInterfaces())
                {
                    if (iface.Name == "IRecolorable")
                    {
                        getSectionColors = iface.GetMethod("getSectionColors");
                        break;
                    }
                }
                if (getSectionColors == null)
                    return false;

                string sectionName = "";
                try
                {
                    BaseField sf = shroudTUSwitch.Fields["sectionName"];
                    if (sf != null)
                        sectionName = sf.GetValue<string>(shroudTUSwitch);
                }
                catch { }

                object result = getSectionColors.Invoke(shroudTUSwitch, new object[] { sectionName });
                Array arr = result as Array;
                if (arr == null || arr.Length < 3)
                    return false;

                channels = new TUChannelColor[3];
                for (int i = 0; i < 3; i++)
                {
                    channels[i] = ReadTUChannel(arr.GetValue(i));
                }
                return true;
            }
            catch { }
            return false;
        }

        TUChannelColor ReadTUChannel(object data)
        {
            TUChannelColor c = new TUChannelColor();
            c.color = Color.white;
            c.specular = 0f;
            c.metallic = 0f;
            c.detail = 1f;
            if (data == null)
                return c;
            try
            {
                Type t = data.GetType();
                FieldInfo cf = t.GetField("color");
                if (cf != null) { object v = cf.GetValue(data); if (v is Color) c.color = (Color)v; }
                FieldInfo sf = t.GetField("specular");
                if (sf != null) { object v = sf.GetValue(data); if (v is float) c.specular = (float)v; }
                FieldInfo mf = t.GetField("metallic");
                if (mf != null) { object v = mf.GetValue(data); if (v is float) c.metallic = (float)v; }
                FieldInfo df = t.GetField("detail");
                if (df != null) { object v = df.GetValue(data); if (v is float) c.detail = (float)v; }
            }
            catch { }
            return c;
        }

        // Fallback: read the default colours declared in the active KSP_TEXTURE_SET's
        // COLORS block (mainColor/secondColor/detailColor = preset names).
        bool TryGetTUDefaultColors(string setName, out TUChannelColor[] channels)
        {
            channels = null;
            if (string.IsNullOrEmpty(setName))
                return false;

            foreach (ConfigNode n in GameDatabase.Instance.GetConfigNodes("KSP_TEXTURE_SET"))
            {
                if (n.GetValue("name") != setName)
                    continue;
                ConfigNode colors = n.GetNode("COLORS");
                if (colors == null)
                    return false;

                channels = new TUChannelColor[3];
                Color c0, c1, c2;
                if (!ResolveTUPresetColor(colors.GetValue("mainColor"), out c0)) c0 = Color.white;
                if (!ResolveTUPresetColor(colors.GetValue("secondColor"), out c1)) c1 = Color.white;
                if (!ResolveTUPresetColor(colors.GetValue("detailColor"), out c2)) c2 = Color.white;
                for (int i = 0; i < 3; i++)
                {
                    channels[i].specular = 0f;
                    channels[i].metallic = 0f;
                    channels[i].detail = 1f;
                }
                channels[0].color = c0;
                channels[1].color = c1;
                channels[2].color = c2;
                return true;
            }
            return false;
        }

        bool ResolveTUPresetColor(string name, out Color color)
        {
            color = Color.white;
            if (string.IsNullOrEmpty(name))
                return false;

            foreach (ConfigNode n in GameDatabase.Instance.GetConfigNodes("KSP_COLOR_PRESET"))
            {
                if (n.GetValue("name") != name)
                    continue;
                string rgb = n.GetValue("color");
                if (string.IsNullOrEmpty(rgb))
                    continue;
                string[] p = rgb.Split(',');
                if (p.Length >= 3)
                {
                    float r, g, b;
                    if (float.TryParse(p[0].Trim(), out r) && float.TryParse(p[1].Trim(), out g) && float.TryParse(p[2].Trim(), out b))
                    {
                        color = new Color(r / 255f, g / 255f, b / 255f);
                        return true;
                    }
                }
            }
            return false;
        }

        void ApplyTUColorsToShroudMats()
        {
            if (shroudMats == null)
                return;

            TUChannelColor[] channels;
            if (!TryGetTUChannelColors(out channels))
            {
                if (!TryGetTUDefaultColors(GetShroudTUSet(), out channels))
                {
                    if (!TryGetTUDefaultColors(DS_SHROUD_TEXTURE_SET, out channels))
                        return;
                }
            }

            cachedMainColor = channels[0].color;
            cachedSecondColor = channels[1].color;
            cachedDetailColor = channels[2].color;
            cachedTUColorsValid = true;

            foreach (Material m in shroudMats)
            {
                if (m == null)
                    continue;

                // TU/Metallic recolour properties: RGB = colour, A = specular
                Color c0 = channels[0].color; c0.a = channels[0].specular;
                Color c1 = channels[1].color; c1.a = channels[1].specular;
                Color c2 = channels[2].color; c2.a = channels[2].specular;
                if (m.HasProperty("_MaskColor1"))
                    m.SetColor("_MaskColor1", c0);
                if (m.HasProperty("_MaskColor2"))
                    m.SetColor("_MaskColor2", c1);
                if (m.HasProperty("_MaskColor3"))
                    m.SetColor("_MaskColor3", c2);
                if (m.HasProperty("_MaskMetallic"))
                    m.SetColor("_MaskMetallic", new Color(channels[0].metallic, channels[1].metallic, channels[2].metallic, 0f));
                if (m.HasProperty("_DetailMult"))
                    m.SetVector("_DetailMult", new Vector4(channels[0].detail, channels[1].detail, channels[2].detail, 0f));
            }
        }

        void PollTURDState()
        {
            if (!HighLogic.LoadedSceneIsEditor || !shroudEnabled)
                return;

            if (!IsShroudTUPaintMode())
                return;

            TUChannelColor[] channels;
            if (!TryGetTUChannelColors(out channels))
                return;

            Color main = channels[0].color;
            Color second = channels[1].color;
            Color detail = channels[2].color;
            if (!cachedTUColorsValid || main != cachedMainColor || second != cachedSecondColor || detail != cachedDetailColor)
            {
                ApplyTUColorsToShroudMats();
            }
        }

        //----------------------------------------------------------------------
        // Shroud flag
        //
        // The player can put one of the game's own flags on the outside of the
        // shroud. KSP's own FlagBrowser MonoBehaviour is reused for the picking,
        // so the window is the stock flag picker.
        //
        // FlagBrowser builds its dialog from Start(): it reads the flag folders
        // out of GameDatabase and calls PopupDialog.SpawnPopupDialog. All that
        // is needed is to add the component to a GameObject and fill in its
        // public OnFlagSelected callback - Unity calls Start() on the next frame
        // and the browser opens. The callback type is the nested
        // FlagBrowser.FlagSelectedCallback, so the delegate is built through
        // reflection from a handler that takes the entry as object.
        //----------------------------------------------------------------------

        GameObject flagBrowserHost;
        //Kept so the flag can be applied even when its url cannot be resolved
        //again through GameDatabase (only valid for the current session)
        Texture2D pickedFlagTexture;

        //The flag replaces only the logo artwork on the LogoHawk7 texture (the
        //same spot the stock hawk/logo art occupies), so the picker is only
        //offered while that texture or its recolour variant is active.
        const string FlagLogoTextureName = "LogoHawk7";
        //Logo area on LogoHawk7.png in top-down pixel coordinates: the blank
        //panel centre (the hawk/"7"/flag artwork was removed from the texture).
        //Must match the black (recolour protected) area of LogoHawk7_RGB, see
        //tools/make_rgb_masks.py
        const int LogoRectX = 96, LogoRectY = 168, LogoRectW = 320, LogoRectH = 176;
        static Texture2D logoBaseTexture;
        static readonly Dictionary<string, Texture2D> flagCompositeCache = new Dictionary<string, Texture2D>();
        static readonly Dictionary<string, Texture2D> flagMaskCache = new Dictionary<string, Texture2D>();

        [KSPEvent(guiName = "#LOC_DecouplerShroud_19", guiActive = false, guiActiveEditor = true)]
        public void selectShroudFlag()
        {
            OpenFlagBrowser();
        }

        void OpenFlagBrowser()
        {
            try
            {
                if (flagBrowserHost != null)
                {
                    Destroy(flagBrowserHost);
                    flagBrowserHost = null;
                }

                GameObject host = new GameObject("DecouplerShroudFlagBrowser");
                flagBrowserHost = host;

                FlagBrowser browser = host.AddComponent<FlagBrowser>();

                FieldInfo selected = typeof(FlagBrowser).GetField("OnFlagSelected", BindingFlags.Instance | BindingFlags.Public);
                if (selected == null)
                {
                    Debug.LogWarning("[DecouplerShroud] FlagBrowser.OnFlagSelected was not found");
                    return;
                }

                MethodInfo handler = typeof(ModuleDecouplerShroud).GetMethod(nameof(onFlagPicked), BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                selected.SetValue(browser, Delegate.CreateDelegate(selected.FieldType, this, handler));

                FieldInfo dismissed = typeof(FlagBrowser).GetField("OnDismiss", BindingFlags.Instance | BindingFlags.Public);
                if (dismissed != null)
                {
                    MethodInfo cancel = typeof(ModuleDecouplerShroud).GetMethod(nameof(onFlagCancelled), BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    dismissed.SetValue(browser, Delegate.CreateDelegate(dismissed.FieldType, this, cancel));
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[DecouplerShroud] Failed to open the flag browser: " + e);
            }
        }

        //Called by KSP's flag browser with the picked FlagBrowser.FlagEntry. The
        //parameter is object because that nested type cannot be named here.
        void onFlagPicked(object entry)
        {
            pickedFlagTexture = ExtractFlagTexture(entry);
            SetShroudFlag(ExtractFlagUrl(entry));
            CloseFlagBrowserHost();
        }

        void onFlagCancelled()
        {
            CloseFlagBrowserHost();
        }

        void CloseFlagBrowserHost()
        {
            if (flagBrowserHost == null)
            {
                return;
            }
            //Delayed so the browser still finishes its own Accept()/Dismiss()
            Destroy(flagBrowserHost, 0.25f);
            flagBrowserHost = null;
        }

        //FlagBrowser.FlagEntry holds a GameDatabase.TextureInfo whose name is the
        //GameDatabase url of the flag.
        static string ExtractFlagUrl(object entry)
        {
            if (entry == null)
            {
                return null;
            }
            try
            {
                FieldInfo ti = entry.GetType().GetField("textureInfo", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                object info = ti == null ? null : ti.GetValue(entry);
                if (info == null)
                {
                    return null;
                }

                foreach (FieldInfo nf in info.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    if (nf.FieldType != typeof(string))
                    {
                        continue;
                    }
                    string s = nf.GetValue(info) as string;
                    if (!string.IsNullOrEmpty(s))
                    {
                        return s;
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[DecouplerShroud] Could not read the picked flag: " + e);
            }
            return null;
        }

        static Texture2D ExtractFlagTexture(object entry)
        {
            if (entry == null)
            {
                return null;
            }
            try
            {
                FieldInfo ti = entry.GetType().GetField("textureInfo", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                object info = ti == null ? null : ti.GetValue(entry);
                if (info == null)
                {
                    return null;
                }
                FieldInfo tf = info.GetType().GetField("texture", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                return tf == null ? null : tf.GetValue(info) as Texture2D;
            }
            catch
            {
                return null;
            }
        }

        void SetShroudFlag(string url)
        {
            if (string.IsNullOrEmpty(url) || url.Equals(shroudFlagURL))
            {
                return;
            }

            shroudFlagURL = url;
            UpdateFlagButtonName();
            changeMaterial();
        }

        void UpdateFlagButtonName()
        {
            BaseEvent flagEvent = Events[nameof(selectShroudFlag)];
            if (flagEvent == null)
            {
                return;
            }

            string label = Localizer.Format("#LOC_DecouplerShroud_19");
            string current = string.IsNullOrEmpty(shroudFlagURL)
                ? Localizer.Format("#LOC_DecouplerShroud_20")
                : FlagDisplayName(shroudFlagURL);

            flagEvent.guiName = label + ": " + current;
            //Only the LogoHawk7 texture has an artwork area to fill
            flagEvent.guiActiveEditor = shroudEnabled && IsLogoShroudTexture();
            flagEvent.guiActive = false;
        }

        static string FlagDisplayName(string url)
        {
            int i = url.LastIndexOf('/');
            return i >= 0 && i < url.Length - 1 ? url.Substring(i + 1) : url;
        }

        //Puts the picked flag only into the logo area of the LogoHawk7 texture.
        //The recolour mask is rebuilt alongside it: black (= protected) covers
        //exactly the painted flag pixels, so the flag keeps its own colours
        //while the whole panel around it follows the TURD colours.
        void ApplyFlagTexture()
        {
            if (shroudMats == null || shroudMats.Length == 0 || shroudMats[0] == null)
            {
                return;
            }
            if (string.IsNullOrEmpty(shroudFlagURL) || !IsLogoShroudTexture())
            {
                return;
            }

            Texture2D composite, mask;
            GetFlagComposite(out composite, out mask);
            if (composite == null)
            {
                return;
            }

            //Same layout as LogoHawk7, so the material keeps its own tiling.
            shroudMats[0].SetTexture("_MainTex", composite);
            if (mask != null)
            {
                shroudMats[0].SetTexture("_MaskTex", mask);
            }
            //The TU/Metallic shader reads _MetallicGlossMap for the protected
            //(mask-black) area, and the stock config points it at LogoHawk7.png
            //itself: its grey pixels act as metallic (~0.65) and its missing
            //alpha as full smoothness -- the flag renders as a polished mirror.
            shroudMats[0].SetTexture("_MetallicGlossMap", MatteGlossMap());
        }

        static Texture2D matteGlossMap;

        //Constant matte, non-metallic gloss map for the flag area.
        static Texture2D MatteGlossMap()
        {
            if (matteGlossMap == null)
            {
                matteGlossMap = new Texture2D(4, 4, TextureFormat.RGBA32, false);
                matteGlossMap.wrapMode = TextureWrapMode.Repeat;
                Color[] px = new Color[16];
                Color matte = new Color(0f, 0f, 0f, 0.25f);
                for (int i = 0; i < px.Length; i++)
                {
                    px[i] = matte;
                }
                matteGlossMap.SetPixels(px);
                matteGlossMap.Apply(false, false);
            }
            return matteGlossMap;
        }

        bool IsLogoShroudTexture()
        {
            var list = ShroudTexture.shroudTextures;
            int i = GetBaseTextureIndex();
            return list != null && i >= 0 && i < list.Count
                && list[i].name.Equals(FlagLogoTextureName);
        }

        //Builds the flag albedo composite and the matching recolour mask in one
        //pass. The albedo has the flag blended into the logo area; the mask is
        //pure PRIMARY except the painted flag pixels, which are BLACK. Only
        //actual flag pixels are protected, so a transparent PNG does not leave
        //a bare base-texture patch behind on a recoloured shroud.
        void GetFlagComposite(out Texture2D albedo, out Texture2D mask)
        {
            albedo = null;
            mask = null;

            Texture2D cached;
            if (flagCompositeCache.TryGetValue(shroudFlagURL, out cached) && cached != null)
            {
                albedo = cached;
                flagMaskCache.TryGetValue(shroudFlagURL, out mask);
                return;
            }

            Texture2D flag = pickedFlagTexture != null ? pickedFlagTexture : GameDatabase.Instance.GetTexture(shroudFlagURL, false);
            if (flag == null)
            {
                Debug.LogWarning("[DecouplerShroud] Shroud flag not found in GameDatabase: " + shroudFlagURL);
                return;
            }

            Texture2D baseTex = LoadLogoBase();
            if (baseTex == null)
            {
                return;
            }

            //Players drop flags in every aspect ratio there is, so keep the
            //flag's own shape, fit it inside the logo area and centre it.
            float fit = Mathf.Min((float)LogoRectW / flag.width, (float)LogoRectH / flag.height);
            int blockW = Mathf.Max(1, Mathf.RoundToInt(flag.width * fit));
            int blockH = Mathf.Max(1, Mathf.RoundToInt(flag.height * fit));
            int blockX = LogoRectX + (LogoRectW - blockW) / 2;
            int blockTop = LogoRectY + (LogoRectH - blockH) / 2;

            Texture2D block = ScaledFlagBlock(flag, blockW, blockH);
            if (block == null)
            {
                return;
            }

            albedo = new Texture2D(baseTex.width, baseTex.height, TextureFormat.RGBA32, false);
            Color[] pixels = baseTex.GetPixels();
            Color[] flagPixels = block.GetPixels();
            //LogoRectY is in top-down (image) coordinates while Unity textures
            //are bottom-up, so flip the rectangle vertically.
            int destY = baseTex.height - blockTop - blockH;
            BlendFlagBlock(pixels, baseTex.width, flagPixels, blockW, blockH, blockX, destY);
            albedo.SetPixels(pixels);
            albedo.Apply(false, false);

            mask = new Texture2D(baseTex.width, baseTex.height, TextureFormat.RGBA32, false);
            Color[] maskPixels = new Color[pixels.Length];
            Color primary = new Color(1f, 0f, 0f, 1f);
            for (int i = 0; i < maskPixels.Length; i++)
            {
                maskPixels[i] = primary;
            }
            for (int row = 0; row < blockH; row++)
            {
                int destRow = (destY + row) * baseTex.width + blockX;
                int flagRow = row * blockW;

                for (int column = 0; column < blockW; column++)
                {
                    if (flagPixels[flagRow + column].a > 0f)
                    {
                        maskPixels[destRow + column] = Color.black;
                    }
                }
            }
            mask.SetPixels(maskPixels);
            mask.Apply(false, false);

            flagCompositeCache[shroudFlagURL] = albedo;
            flagMaskCache[shroudFlagURL] = mask;
        }

        //Transparent PNG flags keep their own alpha, so the flag is blended
        //over the LogoHawk7 base instead of replacing those pixels -- painted
        //over, every unpainted pixel of the flag turns into a black block.
        static void BlendFlagBlock(Color[] dest, int destWidth, Color[] flag, int width, int height, int x, int y)
        {
            for (int row = 0; row < height; row++)
            {
                int destRow = (y + row) * destWidth + x;
                int flagRow = row * width;

                for (int column = 0; column < width; column++)
                {
                    Color f = flag[flagRow + column];
                    if (f.a <= 0f)
                    {
                        continue;
                    }

                    Color d = dest[destRow + column];
                    if (f.a < 1f)
                    {
                        f.r = Mathf.Lerp(d.r, f.r, f.a);
                        f.g = Mathf.Lerp(d.g, f.g, f.a);
                        f.b = Mathf.Lerp(d.b, f.b, f.a);
                    }

                    f.a = 1f;
                    dest[destRow + column] = f;
                }
            }
        }

        //Loads LogoHawk7.png into a readable texture (GameDatabase textures and
        //the runtime-loaded ones are not readable, so this reads the file itself)
        static Texture2D LoadLogoBase()
        {
            if (logoBaseTexture != null)
            {
                return logoBaseTexture;
            }
            try
            {
                string path = KSPUtil.ApplicationRootPath + "GameData/DecouplerShroud/Textures/LogoTextures/LogoHawk7.png";
                Texture2D tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (!tex.LoadImage(System.IO.File.ReadAllBytes(path)))
                {
                    Debug.LogWarning("[DecouplerShroud] Could not decode " + path);
                    Destroy(tex);
                    return null;
                }
                logoBaseTexture = tex;
                return logoBaseTexture;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[DecouplerShroud] Could not load LogoHawk7.png: " + e);
                return null;
            }
        }

        //GameDatabase textures are not CPU-readable, so blit through a render
        //texture to get readable pixels. Scaled here to the logo block size so
        //ReadPixels also does the resize in one step.
        static Texture2D ScaledFlagBlock(Texture2D flag, int w, int h)
        {
            try
            {
                RenderTexture rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32);
                RenderTexture prev = RenderTexture.active;
                Graphics.Blit(flag, rt);
                RenderTexture.active = rt;
                Texture2D block = new Texture2D(w, h, TextureFormat.RGBA32, false);
                block.ReadPixels(new Rect(0f, 0f, w, h), 0, 0);
                block.Apply(false, false);
                RenderTexture.active = prev;
                RenderTexture.ReleaseTemporary(rt);
                return block;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[DecouplerShroud] Could not copy the flag texture: " + e);
                return null;
            }
        }

        void partReattached()
        {
            detectSize();
            if (shroudGO == null)
                createNewShroudGO();

        }

        //Automatically sets size of shrouds
        void detectSize(object arg) { detectSize(); }
        void detectSize()
        {

            if (!HighLogic.LoadedSceneIsEditor)
            {
                return;
            }

            invisibleShroud = false;

            //Check if the size has to be reset
            if (!autoDetectSize || !part.isAttached || !shroudEnabled)
            {
                return;
            }


            thickness = defaultThickness;
            vertOffset = defaultVertOffset;
            //Debug.Log("Defaults: " + defaultBotWidth + ", " + defaultVertOffset);

            if (defaultBotWidth != 0)
            {
                botWidth = defaultBotWidth;
            }
            else
            {
                if (part.collider != null)
                {
                    //botWidth = part.collider.bounds.size.x * part.transform.localScale.x;
                    //botWidth = TrySnapToSize(botWidth, radialSnapMargin);
                    MeshCollider mc = null;
                    if (part.collider is MeshCollider)
                    {
                        mc = (MeshCollider)part.collider;
                    }
                    else
                    {
                        Debug.LogWarning("DecouplerShroud: part collider is " + part.collider.GetType().ToString());
                    }

                    if (mc != null)
                    {
                        //mc.sharedMesh.RecalculateBounds();
                        botWidth = mc.sharedMesh.bounds.size.x * part.transform.localScale.x;

                        //Scale width with scale of parent transforms
                        Transform parentTransform = mc.transform;
                        while (parentTransform != part.transform && parentTransform != null)
                        {
                            botWidth *= parentTransform.localScale.x;
                            parentTransform = parentTransform.parent;
                        }


                        botWidth = getPartRadAtPos(part, GetDecouperShroudedNodeWorldPos(), part.transform.up, .2f) * 2;

                        botWidth = TrySnapToSize(botWidth, radialSnapMargin);
                        //Debug.Log("MeshSize: " + mc.sharedMesh.bounds.size.x + ", Scale: " + shroudAttatchedPart.transform.localScale.x+ ", MeshGO Scale" + mc.transform.localScale.x);
                    }
                    else
                    {
                        botWidth = part.collider.bounds.size.x;
                        botWidth = TrySnapToSize(botWidth, radialSnapMargin);
                        //Debug.Log("Size: " + shroudAttatchedPart.collider.bounds.size.x + ", " + shroudAttatchedPart.transform.localScale.x);
                    }

                }
            }

            //Get part the shroud is attached to
            Part shroudAttatchedPart = GetShroudAttachedPart();

            //World Pos of top attach node, or biggest part of meshcollider
            Vector3 topCenterWorld = Vector3.zero;

            if (shroudAttatchedPart != null)
            {
                //Calculate top Width
                if (shroudAttatchedPart.collider != null)
                {

                    //Check if meshCollider
                    MeshCollider mc = null;
                    if (shroudAttatchedPart.collider is MeshCollider)
                    {
                        mc = (MeshCollider)shroudAttatchedPart.collider;
                    }
                    else
                    {
                        //Debug.LogWarning("[DecouplerShroud] attached collider is "+ shroudAttatchedPart.collider.GetType().ToString());
                    }

                    if (mc != null)
                    {
                        //mc.sharedMesh.RecalculateBounds();
                        /*
						topWidth = mc.sharedMesh.bounds.size.x * shroudAttatchedPart.transform.localScale.x;

						//Scale width with scale of parent transforms
						Transform parentTransform = mc.transform;
						while (parentTransform != shroudAttatchedPart.transform && parentTransform != null) {
							topWidth *= parentTransform.localScale.x;
							parentTransform = parentTransform.parent;
						}*/
                        topWidth = getPartRadAtPos(shroudAttatchedPart, GetShroudattachShroudedNodeWorldPos(), part.transform.up, .2f, out topCenterWorld) * 2;

                        topWidth = TrySnapToSize(topWidth, radialSnapMargin);
                        //Debug.Log("MeshSize: " + mc.sharedMesh.bounds.size.x + ", Scale: " + shroudAttatchedPart.transform.localScale.x+ ", MeshGO Scale" + mc.transform.localScale.x);
                    }
                    else
                    {
                        topWidth = shroudAttatchedPart.collider.bounds.size.x;
                        topWidth = TrySnapToSize(topWidth, radialSnapMargin);
                        //Debug.Log("Size: " + shroudAttatchedPart.collider.bounds.size.x + ", " + shroudAttatchedPart.transform.localScale.x);
                    }

                }

                //============================
                //==== Calculate Height ======
                //============================

                //Get the world position of the node we want to attach to
                AttachNode targetNode = shroudAttatchedPart.FindAttachNodeByPart(GetShroudedPart());

                //Bring the node position to world space
                Vector3 nodeWorldPos = shroudAttatchedPart.transform.TransformPoint(targetNode.position);

                //If we got the top from mesh collider
                if (topCenterWorld != Vector3.zero)
                {
                    nodeWorldPos = topCenterWorld;
                }

                //Get local position of nodeWorldPos
                Vector3 nodeRelativePos = transform.InverseTransformPoint(nodeWorldPos);

                AttachNode topNode = part.FindAttachNode("top");
                if (topNode == null)
                    topNode = part.FindAttachNode("InnerNode");// NO_LOCALIZATION

                //Calculate position of decoupler side
                Vector3 bottomAttachPos = topNode.position + Vector3.up * defaultVertOffset;

                Vector3 differenceVector = nodeRelativePos - bottomAttachPos;
                //Debug.Log("Difference Vector: "+ differenceVector);

                //Set height of shroud to vertical difference between top and bottom node
                height = differenceVector.y;
            }
            else
            {
                //Debug.LogError("Decoupler has no grandparent");
                height = 0;
                invisibleShroud = true;
                topWidth = botWidth;
            }

            //Update shroud mesh
            if (shroudGO != null)
            {
                updateShroud();
            }
        }

        public float getPartRadAtPos(Part p, Vector3 pos, Vector3 nor, float maxDist)
        {
            Vector3 a = Vector3.zero;
            return getPartRadAtPos(p, pos, nor, maxDist, out a);
        }
        public float getPartRadAtPos(Part p, Vector3 pos, Vector3 nor, float maxDist, out Vector3 worldPos)
        {

            MeshCollider mc = p.collider as MeshCollider;
            if (mc == null)
            {
                worldPos = Vector3.zero;
                return 0;
            }
            Vector3 locPos = mc.transform.InverseTransformPoint(pos);
            Vector3 locNor = mc.transform.InverseTransformVector(nor.normalized);
            maxDist *= locNor.magnitude;
            locNor.Normalize();
            //Debug.Log("getPartSizeAtPos: " + p.name + ", " + locPos + ", "+locNor+", " + nor+", "+mc.sharedMesh.vertices.Length);

            Vector3 minV = Vector3.one * 10000;
            bool inRange = false;
            Vector3 maxRad = Vector3.zero;
            worldPos = Vector3.zero;
            foreach (Vector3 v in mc.sharedMesh.vertices)
            {
                Vector3 d = v - locPos;
                float dist = Vector3.Dot(d, locNor);
                if (Mathf.Abs(dist) < maxDist)
                {
                    inRange = true;
                    Vector3 rad = (d - dist * locNor);
                    if (rad.magnitude > maxRad.magnitude)
                    {
                        maxRad = rad;
                        worldPos = v - rad;
                    }
                }
                else
                {
                    if (Mathf.Abs(dist) < Vector3.Dot(minV, locNor))
                    {
                        minV = d;
                    }
                }
            }
            if (!inRange)
            {
                //Debug.Log("None in range "+minV);
            }
            //Debug.Log(maxRad+" _ "+mc.transform.TransformVector(maxRad)+"\n");
            worldPos = mc.transform.TransformPoint(worldPos);
            return mc.transform.TransformVector(maxRad).magnitude;
        }

        public float TrySnapToSize(float size, float margin)
        {

            foreach (float snap in snapSizes)
            {
                if (Math.Abs(snap - size) < margin * size)
                {
                    return snap;
                }
            }

            return size;
        }

        //If decoupler is detached from engine, remove shroud and reenable stock shrouds
        void partDetached()
        {
            if (GetShroudedPart() == null)
            {
                destroyShroud();
                if (shroudEnabled && engineShrouds != null)
                {
                    if (engineShrouds.Length > 0)
                    {
                        foreach (ModuleJettison engineShroud in engineShrouds)
                        {
                            engineShroud.shroudHideOverride = turnedOffEngineShroud;
                        }
                    }
                }
            }
        }

        public void OnDestroy()
        {
            destroyShroud();
        }

        void destroyShroud()
        {
            if (shroudGO != null)
            {
                Destroy(shroudGO);
            }
            if (shroudMats != null)
            {
                for (int i = 0; i < shroudMats.Length; i++)
                {
                    if (shroudMats[i] != null)
                    {
                        Destroy(shroudMats[i]);
                    }
                }
            }
            shroudGO = null;
            shroudMats = null;
        }

        //Generates the shroud for the first time
        void generateShroud()
        {
            shroudShaper = new ShroudShaper(this, nSides);
            shroudShaper.generate();
        }

        //updates the shroud mesh when values changed
        void updateShroud(object arg) { updateShroud(); }
        void updateShroud()
        {
            if (!shroudEnabled)
            {
                destroyShroud();
            }
            if (shroudGO == null || shroudShaper == null)
            {
                createNewShroudGO();
            }

            updateTextureScale();

            shroudShaper.update();
            if (shroudGO != null)
            {
                shroudGO.SetActive(!invisibleShroud);
            }

            if (isFarInstalled())
            {
                //Debug.Log("[Debug] Updating FAR Voxels");
                #region NO_LOCALIZATION
                part.SendMessage("GeometryPartModuleRebuildMeshData");
                #endregion
            }
        }

        //Recalculates the drag cubes for the model
        void generateDragCube()
        {


            if (isFarInstalled())
            {
                //Debug.Log("[Debug] Updating FAR Voxels");
                #region NO_LOCALIZATION
                part.SendMessage("GeometryPartModuleRebuildMeshData");
                #endregion
            }

            if (shroudEnabled && HighLogic.LoadedSceneIsFlight)
            {
                //Calculate dragcube for the cone manually
                DragCube dc = DragCubeSystem.Instance.RenderProceduralDragCube(part);
                part.DragCubes.ClearCubes();
                part.DragCubes.Cubes.Add(dc);
                part.DragCubes.ResetCubeWeights();
                part.DragCubes.ForceUpdate(true, true, false);

            }
        }

        //Create the gameObject with the meshrenderer
        void createNewShroudGO()
        {

            //Debug.Log("[Debug] createNewShroudGO called, shroudEnabled: "+shroudEnabled+", jettisoned: "+jettisoned);

            if (!shroudEnabled || jettisoned)
            {
                return;
            }

            #region NO_LOCALIZATION
            AttachNode topNode = part.FindAttachNode("top");
            if (topNode == null)
                topNode = part.FindAttachNode("InnerNode");
            #endregion

            generateShroud();

            if (shroudGO != null)
            {
                Destroy(shroudGO);
            }

            shroudGO = new GameObject(DECOUPLERSHROUD_GO_NAME);
            shroudGO.transform.parent = part.FindModelComponent<Transform>();
            shroudGO.transform.localPosition = topNode.position;
            shroudGO.transform.localRotation = Quaternion.identity;

            if (shroudMats != null)
            {
                foreach (Material mat in shroudMats)
                {
                    Destroy(mat);
                }
            }
            shroudMats = null;

            //Create Segment GameObjects
            for (int i = 0; i < segments; i++)
            {
                GameObject segment = new GameObject("ShroudSegment: " + i);
                segment.transform.parent = shroudGO.transform;
                segment.transform.localPosition = Vector3.zero;
                segment.transform.localRotation = Quaternion.identity;
                segment.AddComponent<MeshFilter>().mesh = shroudShaper.multiCylinder.meshes[i];
                segment.AddComponent<MeshRenderer>();

                //Create Gameobjects with meshColliders if collisionEnabled
                if (collisionEnabled)
                {
                    for (int j = 0; j < collPerSegment; j++)
                    {
                        GameObject segColl = new GameObject("SegColl");
                        segColl.transform.parent = segment.transform;
                        segColl.transform.localPosition = Vector3.zero;
                        segColl.transform.localRotation = Quaternion.identity;
                        segColl.AddComponent<MeshCollider>();
                        segColl.GetComponent<MeshCollider>().sharedMesh = shroudShaper.collCylinder.meshes[i * collPerSegment + j];
                        segColl.GetComponent<MeshCollider>().convex = true;

                        //segColl.AddComponent<MeshFilter>().sharedMesh = shroudCylinders.collCylinder.meshes[i * collPerSegment + j];
                        //segColl.AddComponent<MeshRenderer>();
                    }
                }
            }
            //Setup materials
            changeMaterial();

            Fix_SegmentChangedCallUpdateTexture = 5;

            generateDragCube();

            shroudGO.SetActive(!invisibleShroud);
        }


        void UpdateMaterialsOpacity()
        {
            if (!HighLogic.LoadedSceneIsEditor || shroudMats == null)
            {
                return;
            }

            //Ugly Fix
            if (--Fix_SegmentChangedCallUpdateTexture > 0)
            {
                updateTextureScale();
            }

            float alpha = distPointRay(transform.TransformPoint((vertOffset + height) / 2f * Vector3.up), Camera.main.ScreenPointToRay(Input.mousePosition)) / (botWidth + topWidth + height) * 2;
            alpha = Mathf.Clamp(alpha, editorMinAlpha, 1);

            foreach (Material m in shroudMats)
            {
                if (m == null)
                {
                    Debug.LogWarning("DecouplerShroud: Material in shroudMats is null");
                    continue;
                }
                m.SetFloat("_Opacity", alpha); // NO_LOCALIZATION

            }
        }

        float distPointRay(Vector3 p, Ray r)
        {
            return Vector3.Cross(r.direction, p - r.origin).magnitude / r.direction.magnitude;
        }

        bool destroyShroudIfNoTopNode()
        {
            #region NO_LOCALIZATION
            AttachNode topNode = part.FindAttachNode("top");
            if (topNode == null)
            {
                topNode = part.FindAttachNode("InnerNode");
                #endregion
                if (topNode == null)
                {
                    Debug.LogError("DecouplerShroud: Decoupler is missing top node!");
                    Debug.LogError("DecouplerShroud: Removing Decouplershroud from part: " + part.name);
                    part.RemoveModule(this);
                    Destroy(this);
                    return true;
                }
            }
            return false;
        }

        Part GetShroudedPart()
        {
            #region NO_LOCALIZATION
            AttachNode topNode = part.FindAttachNode("top");
            if (topNode == null)
            {
                topNode = part.FindAttachNode("InnerNode");
                #endregion
                if (topNode == null)
                {
                    Debug.LogError("Decoupler is missing top node!");
                    return null;
                }
            }
            if (topNode.owner == (part))
            {
                return topNode.attachedPart;
            }
            else
            {
                return topNode.owner;
            }
        }

        Part GetShroudAttachedPart()
        {
            Part shroudedPart = GetShroudedPart();
            if (shroudedPart == null)
            {
                return null;
            }

            #region NO_LOCALIZATION
            AttachNode shroudedTopNode = shroudedPart.FindAttachNode("top");
            AttachNode shroudedBotNode = shroudedPart.FindAttachNode("bottom");
            if (shroudedTopNode == null)
            {
                shroudedTopNode = shroudedPart.FindAttachNode("InnerNode");
                shroudedBotNode = shroudedPart.FindAttachNode("OuterNode");
            }
            #endregion

            Part shroudAttatchPart = null;
            if (shroudedTopNode != null)
            {
                if (shroudedTopNode.owner == shroudedPart)
                {
                    shroudAttatchPart = shroudedTopNode.attachedPart;
                }
                else
                {
                    shroudAttatchPart = shroudedTopNode.owner;
                }
            }
            if (shroudedBotNode != null)
            {
                if (shroudAttatchPart == part || shroudAttatchPart == null)
                {
                    shroudAttatchPart = shroudedBotNode.owner;
                    if (shroudAttatchPart == shroudedPart)
                    {
                        shroudAttatchPart = shroudedBotNode.attachedPart;
                    }
                }
                if (shroudAttatchPart == part)
                {
                    shroudAttatchPart = null;
                }
            }


            return shroudAttatchPart;

        }

        Vector3 GetDecouperShroudedNodeWorldPos()
        {
            #region NO_LOCALIZATION
            AttachNode topNode = part.FindAttachNode("top");
            if (topNode == null)
                topNode = part.FindAttachNode("InnerNode");
            #endregion
            return part.transform.TransformPoint(topNode.position);
        }
        Vector3 GetShroudattachShroudedNodeWorldPos()
        {
            Part attached = GetShroudAttachedPart();
            AttachNode an = attached.FindAttachNodeByPart(GetShroudedPart());
            if (an != null)
            {
                return attached.transform.TransformPoint(an.position);
            }
            return Vector3.zero;
        }

        public bool ClosedAndLocked()
        {
            return shroudEnabled && GetShroudedPart() != null;
        }

        public Vessel GetVessel()
        {
            return vessel;
        }

        public Part GetPart()
        {
            return part;
        }

        public static bool FARinstalled, FARchecked;
        public static bool isFarInstalled()
        {

            if (!FARchecked)
            {
                var asmlist = AssemblyLoader.loadedAssemblies;

                if (asmlist != null)
                {
                    for (int i = 0; i < asmlist.Count; i++)
                    {                        
                        if (asmlist[i].name == "FerramAerospaceResearch") // NO_LOCALIZATION

                        {
                            FARinstalled = true;

                            break;
                        }
                    }
                }
                //Debug.Log("[Debug] Far installed: "+FARinstalled);
                FARchecked = true;
            }

            return FARinstalled;
        }

    }
}
