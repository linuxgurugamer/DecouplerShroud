﻿using System.Collections.Generic;
using UnityEngine;

namespace DecouplerShroud {
	class ShroudTexture {
		public static List<ShroudTexture> shroudTextures;


		public string name = "ERR";
        public string displayName = "ERR";
		public List<SurfaceTexture> textures;
		
		public ShroudTexture() {
			textures = new List<SurfaceTexture>();
		}

		public static void LoadTextures() {
			if (shroudTextures == null) {
				List<GameDatabase.TextureInfo> textures = GameDatabase.Instance.GetAllTexturesInFolder("DecouplerShroud/Textures/");
				shroudTextures = new List<ShroudTexture>();


				Dictionary<ConfigNode, string> waiting = new Dictionary<ConfigNode, string>();
				foreach(ConfigNode node in GameDatabase.Instance.GetConfigNodes("ShroudTexture")) {

					if (node.HasValue("base")) {
						string texBase = node.GetValue("base").Trim();
						if (getShroudTextureByName(texBase) == null) {
							waiting.Add(node, texBase);
							continue;
						}
						ParseShroudTextureWithTexBase(node, getShroudTextureByName(texBase), waiting);
					} else {
						ParseShroudTexture(node, waiting);
					}
				}
			}
		}

		static ShroudTexture getShroudTextureByName(string name) {
			foreach (ShroudTexture s in shroudTextures) {
				if (s.name == name) {
					return s;
				}
			}
			
			return null;
		}

		static void ParseShroudTexture(ConfigNode node, Dictionary<ConfigNode, string> waiting) {
			int v = 1;
			if (node.HasValue("v"))
				int.TryParse(node.GetValue("v"), out v);

			if (!node.HasNode("outside") || !node.HasNode("top") || !node.HasNode("inside")) {
				Debug.LogError("[DecouplerShroud] texture config missing node: " + node);
				return;
			}

			ShroudTexture tex = new ShroudTexture();
			if (node.HasValue("name")) {
				tex.name = node.GetValue("name");
                tex.displayName = tex.name;
                if (getShroudTextureByName(tex.name) != null) {
                    Debug.LogError("[DecouplerShroud] ShroudTexture already exists with name: "+ tex.name);
					return;
                }

			} else {
				Debug.LogError("[DecouplerShroud] ShroudTexture needs Name");
				return;
			}

            if (node.HasValue("displayName")) {
                tex.displayName = node.GetValue("displayName");
				Debug.Log("Changed Display Name to: " + tex.displayName);
            }

			if (!node.HasNode("outside") || !node.HasNode("top") || !node.HasNode("inside")) {
				Debug.LogError("[DecouplerShroud] texture config needs outside, top, inside nodes if no base is given. Texutre Name: " + tex.name);
				return;
			}

            tex.textures.Add(new SurfaceTexture(node.GetNode("outside"), v));
			tex.textures.Add(new SurfaceTexture(node.GetNode("top"), v));
			tex.textures.Add(new SurfaceTexture(node.GetNode("inside"), v));
			shroudTextures.Add(tex);
			Debug.Log("[DecouplerShroud] Loaded ShroudedTexture: " + tex.name);


			if (waiting.ContainsValue(tex.name)) {
				foreach (KeyValuePair<ConfigNode, string> p in waiting) {
					if (p.Value == tex.name) {
						ParseShroudTextureWithTexBase(p.Key, tex, waiting);
					}
				}
			}
		}

		static void ParseShroudTextureWithTexBase(ConfigNode node, ShroudTexture texBase, Dictionary<ConfigNode, string> waiting) {
			int v = 1;
			if (node.HasValue("v"))
				int.TryParse(node.GetValue("v"), out v);

			ShroudTexture tex = new ShroudTexture();
			if (node.HasValue("name")) {
				tex.name = node.GetValue("name");
				tex.displayName = tex.name;
				if (getShroudTextureByName(tex.name) != null) {
					Debug.LogError("[DecouplerShroud] ShroudTexture already exists with name: " + tex.name);
					return;
				}
			} else {
				Debug.LogError("[DecouplerShroud] ShroudTexture needs Name");
				return;
			}
			if (node.HasValue("displayName"))
            {
                tex.displayName = node.GetValue("displayName");
            }

            tex.textures.Add(new SurfaceTexture(node.GetNode("outside"), texBase.textures[0], v));
			tex.textures.Add(new SurfaceTexture(node.GetNode("top"), texBase.textures[1], v));
			tex.textures.Add(new SurfaceTexture(node.GetNode("inside"), texBase.textures[2], v));
			shroudTextures.Add(tex);
			Debug.Log("[DecouplerShroud] Loaded ShroudedTexture: "+tex.name + " with base: "+texBase.name);

			if (waiting.ContainsValue(tex.name)) {
				foreach (KeyValuePair<ConfigNode, string> p in waiting) {
					if (p.Value == tex.name) {
						ParseShroudTextureWithTexBase(p.Key, tex, waiting);
					}
				}
			}
		}

	}
}
