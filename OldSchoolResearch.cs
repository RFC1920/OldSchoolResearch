#region License (GPL v2)
/*
    DESCRIPTION
    Copyright (c) 2023 RFC1920 <desolationoutpostpve@gmail.com>

    program is free software; you can redistribute it and/or
    modify it under the terms of the GNU General Public License v2.

    program is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with program; if not, write to the Free Software
    Foundation, Inc., 51 Franklin Street, Fifth Floor, Boston, MA  02110-1301, USA.

    Optionally you can also view the license at <http://www.gnu.org/licenses/>.
*/
#endregion License Information (GPL v2)
using Facepunch;
using Oxide.Core;
using Oxide.Core.Libraries.Covalence;
using Oxide.Game.Rust.Cui;
using Rust;
using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using VLB;

namespace Oxide.Plugins
{
    [Info("Old School Research", "RFC1920", "1.0.2")]
    [Description("")]
    internal class OldSchoolResearch : RustPlugin
    {
        private ConfigData configData;
        public static OldSchoolResearch Instance;

        private const string permUse = "oldschoolresearch.use";
        private List<uint> tables = new List<uint>();
        private List<ulong> canres = new List<ulong>();

        private const string OSRGUI = "osr.gui";
        private const string OSRGUI2 = "osr.gui2";
        private const string OSRGUI3 = "osr.gui3";
        private Dictionary<uint, ulong> rsloot = new Dictionary<uint, ulong>();
        private Dictionary<ulong, Timer> rstimer = new Dictionary<ulong, Timer>();

        #region Message
        private string Lang(string key, string id = null, params object[] args) => string.Format(lang.GetMessage(key, this, id), args);
        private void Message(IPlayer player, string key, params object[] args) => player.Message(Lang(key, player.Id, args));
        private void LMessage(IPlayer player, string key, params object[] args) => player.Reply(Lang(key, player.Id, args));
        #endregion

        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["chance"] = "Chance: {0}%",
                ["cost"] = "Cost: {0}",
                ["beginres"] = "Begin Research",
                ["failed"] = "FAILED!",
                ["notauthorized"] = "You don't have permission to do that !!"
            }, this);
        }

        private void OnServerInitialized()
        {
            permission.RegisterPermission(permUse, this);
            LoadConfigVariables();
            LoadData();
            Instance = this;

            foreach (uint table in tables)
            {
                DoLog($"Activating table {table}");
                BaseNetworkable ent = BaseNetworkable.serverEntities.Find(table);
                if (ent != null && ent is ResearchTable)
                {
                    ResearchTable rst = ent as ResearchTable;
                    //UnityEngine.Object.Destroy(rst?.gameObject.GetComponent<ResearchTableMod>());
                    //rst?.gameObject?.AddComponent<ResearchTableMod>();
                    rst?.gameObject?.GetOrAddComponent<ResearchTableMod>();
                    rst?.SetActive(true);
                }
            }

            AddCovalenceCommand("begin_research", "BeginResearch");
        }

        private void BeginResearch(IPlayer player, string command, string[] args)
        {
            if (args.Length == 1)
            {
                uint tableId = uint.Parse(args[0]);
                if (tables.Contains(tableId))
                {
                    ResearchTableMod rtm = BaseNetworkable.serverEntities.Find(tableId).GetComponent<ResearchTableMod>();
                    if (rtm != null)
                    {
                        ResearchTable rt = rtm.GetComponent<ResearchTable>();
                        Item item = rt?.inventory?.GetSlot(0);
                        if (item != null)
                        {
                            DoLog($"Player trying to research {item.info.name} on table {tableId}");
                            if ((player.Object as BasePlayer).blueprints.HasUnlocked(item.info))
                            {
                                DoLog("Already researched");
                                return;
                            }

                            DoLog("Trying to begin research routine");
                            rtm.BeginResearch();
                            return;
                        }
                        DoLog("No item present");
                    }
                }
            }
        }

        private bool CanPickupEntity(BasePlayer player, ResearchTable entity)
        {
            ResearchTableMod rtm = entity.GetComponent<ResearchTableMod>();
            if (rtm == null) return true;
            DoLog("CanPickupEntity called on RTM");
            if (tables.Contains(entity.net.ID))
            {
                tables.Remove(entity.net.ID);
                SaveData();
            }
            return true;
        }

        private object CanLootEntity(BasePlayer player, ResearchTable rst)
        {
            if (rst == null) return null;
            ResearchTableMod rtm = rst.GetComponent<ResearchTableMod>();
            if (rtm == null) return null;

            if (rsloot.ContainsKey(rst.net.ID)) return null;
            rsloot.Add(rst.net.ID, player.userID);

            // rtm.OpenContainer(player);
            Item item = rst.inventory.GetSlot(0);
            Item currency = rst.inventory.GetSlot(1);
            if (item != null)
            {
                DoLog($"Creating CheckLooting timer for {player.displayName}");
                rstimer.Remove(player.userID);
                rstimer.Add(player.userID, timer.Every(0.5f, () => CheckLooting(player)));
                rtm.ItemLoaded(player, rst.net.ID);
            }

            return null;
        }

        private void RsGUI(BasePlayer player, uint rst, string label = null, bool failed = false)
        {
            CuiHelper.DestroyUi(player, OSRGUI);
            CuiHelper.DestroyUi(player, OSRGUI2);
            CuiHelper.DestroyUi(player, OSRGUI3);

            DoLog($"GUI opened for table {rst}");
            CuiElementContainer container = UI.Container(OSRGUI, UI.Color("3E3C37", 1f), "0.85 0.405", "0.9465 0.515", false, "Overlay");

            ResearchTable rt = BaseNetworkable.serverEntities.Find(rst) as ResearchTable;
            ResearchTableMod rtm = rt?.gameObject.GetComponent<ResearchTableMod>();

            UI.Label(ref container, OSRGUI, UI.Color("#DDDDDD", 1f), Lang("cost", null, rtm?.ResearchCost.ToString()), 22, "0 0.5", "1 1");
            UI.Label(ref container, OSRGUI, UI.Color("#DDDDDD", 1f), Lang("chance", null, rtm?.Chance.ToString()), 20, "0 0", "1 0.5");

            CuiHelper.AddUi(player, container);

            CuiElementContainer cont3 = UI.Container(OSRGUI2, UI.Color("454545", 1f), "0.657 0.163", "0.94 0.205", false, "Overlay");
            UI.Button(ref cont3, OSRGUI2, UI.Color("FFFFFF", 0.3f), Lang("beginres"), 14, "0.6 0", "1 1", $"begin_research {rst} ");
            CuiHelper.AddUi(player, cont3);

            CuiElementContainer cont2 = UI.Container(OSRGUI3, UI.Color("3E3C37", 1f), "0.77 0.221", "0.9465 0.28", false, "Overlay");
            if (failed)
            {
                UI.Label(ref cont2, OSRGUI3, UI.Color("FF0000", 1f), Lang("FAILED"), 18, "0 0.5", "1 1");
            }
            CuiHelper.AddUi(player, cont2);
        }

        private void CheckLooting(BasePlayer player)
        {
            // Fail-safe removal of GUI if loot table no longer open
            if (!player.inventory.loot.IsLooting())
            {
                DoLog("Not looting.  Killing GUI and CheckLooting timer...");
                CuiHelper.DestroyUi(player, OSRGUI);
                CuiHelper.DestroyUi(player, OSRGUI2);
                CuiHelper.DestroyUi(player, OSRGUI3);
                player.EndLooting();
                rstimer[player.userID].Destroy();
                rstimer.Remove(player.userID);
            }
        }

        //private object CanResearchItem(BasePlayer player, Item item)
        //{
        //    if (player == null) return null;
        //    if (item == null) return null;
        //    if (player.blueprints.HasUnlocked(item.info))
        //    {
        //        return true;
        //    }
        //    canres.Add(player.userID);
        //    return null;
        //}

        private void OnLootEntityEnd(BasePlayer player, BaseCombatEntity entity)
        {
            if (player == null) return;
            if (!(entity is ResearchTable)) return;
            // This setup is causing occaisional continuance of the GUI when looting the RT has ended...
            ulong networkID;
            if (entity == null || !rsloot.TryGetValue(entity.net.ID, out networkID))
            {
                return;
            }

            if (networkID == player.userID)
            {
                CuiHelper.DestroyUi(player, OSRGUI);
                CuiHelper.DestroyUi(player, OSRGUI2);
                CuiHelper.DestroyUi(player, OSRGUI3);

                if (rsloot.ContainsKey(entity.net.ID))
                {
                    rsloot.Remove(entity.net.ID);
                }
                if (canres.Contains(player.userID))
                {
                    canres.Remove(player.userID);
                }
            }
        }

        private void OnEntityBuilt(Planner plan, GameObject go)
        {
            BasePlayer player = plan?.GetOwnerPlayer();
            if (player == null) return;
            if (!permission.UserHasPermission(player.UserIDString, permUse)) return;

            ResearchTable table = go.GetComponent<ResearchTable>();
            if (table != null)
            {
                DoLog("OEB");
                table.researchResource = ItemManager.FindItemDefinition(configData.currency);
                go.GetOrAddComponent<ResearchTableMod>();
                if (!tables.Contains(table.net.ID)) tables.Add(table.net.ID);
                SaveData();
            }
        }

        ItemContainer.CanAcceptResult? CanAcceptItem(ItemContainer container, Item item, int targetPos)
        {
            ResearchTable r = container?.entityOwner as ResearchTable;
            ResearchTableMod rtm = r?.GetComponent<ResearchTableMod>();
            if (rtm == null) return null;
            DoLog("CanAcceptItem called");
            BasePlayer player = item.GetOwnerPlayer();
            DoLog($"Calling item loaded on {rtm.table.net.ID} for player {player?.displayName}");
            if (player != null)
            {
                if (item.info.name == configData.currency && targetPos == 1) // FIXME
                {
                    item.MoveToContainer(container, 1);
                    container.MarkDirty();
                }
                NextTick(() => rtm?.ItemLoaded(player, r.net.ID));
            }
            return null;
        }

        private object CanMoveItem(Item item, PlayerInventory playerLoot, uint targetContainerID, int targetSlot, int amount)
        {
            BasePlayer player = playerLoot.GetComponent<BasePlayer>();
            BaseEntity sourceContainer = item.GetEntityOwner();
            BaseNetworkable targetContainer = BaseNetworkable.serverEntities.Find(targetContainerID);

            if (sourceContainer != null && sourceContainer is ResearchTable)
            {
                Puts($"{player.displayName} moving {item.info.name} from {sourceContainer?.ShortPrefabName}");
                ResearchTableMod rtm = sourceContainer.GetComponent<ResearchTableMod>();
                NextTick(() => rtm?.ItemLoaded(player, sourceContainer.net.ID));
            }
            else if (targetContainer != null && targetContainer is ResearchTable)
            {
                Puts($"{player.displayName} moving {item.info.name} to {targetContainer?.ShortPrefabName}");
                ResearchTableMod rtm = targetContainer.GetComponent<ResearchTableMod>();
                NextTick(() => rtm?.ItemLoaded(player, targetContainer.net.ID));
            }
            return null;
        }
        //    Puts($"Finding container {targetContainer}");
        //    ItemContainer container = playerLoot.FindContainer(targetContainer);

        //    Puts($"Finding rt");
        //    ResearchTable r = container?.entityOwner as ResearchTable;
        //    Puts("Finding rtm");
        //    var rtm = r.GetComponent<ResearchTableMod>();
        //    // Update cost based on loaded item
        //    Puts("Calling item loaded");
        //    //rtm?.ItemLoaded(player, rtm.table.net.ID);
        //    return null;
        //}

        //private void OnEntitySpawned(ResearchTable table)
        //{
        //    Puts("OES WORKS");
        //        table.researchResource = ItemManager.FindItemDefinition("ducttape");
        //    Puts(table.researchResource.displayName.english);
        //}

        private void SaveData()
        {
            Interface.Oxide.DataFileSystem.WriteObject(Name + "/tables", tables);
        }

        private void LoadData()
        {
            tables = Interface.Oxide.DataFileSystem.ReadObject<List<uint>>(Name + "/tables");
        }

        private void DoLog(string message)
        {
            if (configData.debug)
            {
                Interface.Oxide.LogInfo(message);
            }
        }

        private void Unload()
        {
            foreach (BasePlayer player in BasePlayer.activePlayerList)
            {
                CuiHelper.DestroyUi(player, OSRGUI);
                CuiHelper.DestroyUi(player, OSRGUI2);
                CuiHelper.DestroyUi(player, OSRGUI3);
            }
            DestroyAll<ResearchTableMod>();
        }

        private void DestroyAll<T>() where T : MonoBehaviour
        {
            foreach (T type in UnityEngine.Object.FindObjectsOfType<T>())
            {
                UnityEngine.Object.Destroy(type);
            }
        }

        private class ConfigData
        {
            //public Options Options;
            public bool debug;
            public string currency;
            public VersionNumber Version;
        }

        private void LoadConfigVariables()
        {
            configData = Config.ReadObject<ConfigData>();

            configData.Version = Version;
            SaveConfig(configData);
        }

        protected override void LoadDefaultConfig()
        {
            Puts("Creating new config file.");
            ConfigData config = new ConfigData()
            {
                debug = false,
                currency = "scrap"
            };

            SaveConfig(config);
        }

        private void SaveConfig(ConfigData config)
        {
            Config.WriteObject(config, true);
        }

        // HERE
        public class ResearchTableMod : FacepunchBehaviour
        {
            private StorageContainer parent;
            public ResearchTable table;
            private Item ResearchItem;
            public int ResearchCost;
            private int Currency;
            public int Chance;
            private System.Random random;
            private bool researchActive;
            private bool researchComplete;

            public void Awake()
            {
                table = GetComponent<ResearchTable>();
                parent = table?.gameObject.GetComponent<StorageContainer>();
                random = new System.Random();
                table.researchResource = ItemManager.FindItemDefinition(Instance.configData.currency);
            }

            public void ItemLoaded(BasePlayer player, uint rst)
            {
                Instance.DoLog($"ItemLoaded called for player {player?.displayName} on table {rst}");
                ResearchItem = table?.inventory.GetSlot(0);
                Instance.DoLog($"Got type {ResearchItem?.GetType()}");
                Instance.DoLog($"Calculating cost for {ResearchItem?.info.name}");
                if (!table.IsItemResearchable(ResearchItem)) return;

                // Check currency slot
                if (table?.inventory.GetSlot(1) == null)
                {
                    Currency = 0;
                }
                else
                {
                    Currency = table.inventory.GetSlot(1).amount;
                    Instance.DoLog($"Currency == {Currency}");
                    Chance = 1;
                }

                if (ResearchItem != null)
                {
                    ResearchCost = GetResearchCost(ResearchItem);
                }

                Chance = (int)Math.Abs((float)((double)Currency / (double)ResearchCost * 100));
                if (Chance > 100) Chance = 100;
                Instance.DoLog($"Reload GUI with ResearchCost of {ResearchCost}, Currency of {Currency}, and chance of {Chance}%");
                Instance.RsGUI(player, rst);
            }

            public void BeginResearch()
            {
                if (table?.inventory.GetSlot(0) != null && table?.inventory.GetSlot(1) != null && !researchActive)
                {
                    ResearchItem = table?.inventory.GetSlot(0);
                    if (!table.IsItemResearchable(ResearchItem)) return;

                    // Check currency slot
                    if (table?.inventory.GetSlot(1) != null)
                    {
                        Currency = table.inventory.GetSlot(1).amount;
                    }

                    if (Currency > 0)
                    {
                        ResearchCost = GetResearchCost(ResearchItem);
                    }

                    //Instance.DoLog($"Researching {ResearchItem.info.displayName.english}");
                    //basePlayer.inventory.loot.SendImmediate();
                    RealResearch();
                    return;
                }
                Instance.DoLog("Research slot was empty or research already in progress");
            }

            private int GetResearchCost(Item item)
            {
                if (item.info.rarity == Rarity.Common)
                {
                    ResearchCost = 20;
                }
                if (item.info.rarity == Rarity.Uncommon)
                {
                    ResearchCost = 75;
                }
                if (item.info.rarity == Rarity.Rare)
                {
                    ResearchCost = 125;
                }
                if (item.info.rarity == Rarity.VeryRare || item.info.rarity == Rarity.None)
                {
                    ResearchCost = 500;
                }
                ItemBlueprint itemBlueprint = ItemManager.FindBlueprint(item.info);
                if (itemBlueprint?.defaultBlueprint == true)
                {
                    return ConVar.Server.defaultBlueprintResearchCost;
                }
                return ResearchCost;
            }

            public void RealResearch()
            {
                if (table.researchStartEffect.isValid)
                {
                    Effect.server.Run(table.researchStartEffect.resourcePath, parent, 0, Vector3.zero, Vector3.zero, null, false);
                }
                table.researchFinishedTime = UnityEngine.Time.realtimeSinceStartup + table.researchDuration;
                parent.Invoke(new Action(table.ResearchAttemptFinished), table.researchDuration);

                Instance.DoLog($"Researching {ResearchItem.info.name} for {Currency} {table.researchResource.name}");
                parent.inventory.SetLocked(true);
                parent.SetFlag(BaseEntity.Flags.On, true, false, true);
                parent.SendNetworkUpdate(BasePlayer.NetworkQueue.Update);

                researchComplete = false;
                researchActive = true;
                Instance.timer.Once(10f, () => researchComplete = true);

                // HERE
                Instance.DoLog($"Chance of success is {Chance}");
            }

            public void FixedUpdate()
            {
                if (!table.IsOpen())
                {
                    //Instance.Puts("NOT OPEN");
                    return;
                }
                //Instance.Puts("TABLE OPEN");

                if (table.IsResearching())
                {
                    Instance.DoLog("RESEARCHING");
                }
                if (researchActive && researchComplete)
                {
                    Instance.DoLog("FINISHED!");
                    researchActive = false;
                    researchComplete = false;

                    if (random.Next(0, 100) < Chance)
                    {
                        Instance.DoLog("SUCCESS!");
                        if (table.researchSuccessEffect.isValid)
                        {
                            Effect.server.Run(table.researchSuccessEffect.resourcePath, parent, 0, Vector3.zero, Vector3.zero, null, false);
                        }
                        Instance.DoLog($"Creating BP for {ResearchItem.info.name}");
                        Item newbp = ItemManager.CreateByName("blueprintbase");
                        newbp.blueprintTarget = ResearchItem.info.itemid;
                        //ResearchItem.RemoveFromContainer();
                        table?.inventory.GetSlot(0).RemoveFromContainer();
                        newbp.MoveToContainer(table.inventory, 0);
                        Chance = 0;
                        //table.EndResearch();
                    }
                    else if (table.researchFailEffect.isValid)
                    {
                        Instance.DoLog("FAILED!");
                        Effect.server.Run(table.researchFailEffect.resourcePath, parent, 0, Vector3.zero, Vector3.zero, null, false);
                    }
                    table?.inventory.GetSlot(1)?.RemoveFromContainer();
                }
            }
        }

        #region UICLASS
        public static class UI
        {
            public static CuiElementContainer Container(string panel, string color, string min, string max, bool useCursor = false, string parent = "Overlay")
            {
                return new CuiElementContainer()
                {
                    {
                        new CuiPanel
                        {
                            Image = { Color = color },
                            RectTransform = {AnchorMin = min, AnchorMax = max},
                            CursorEnabled = useCursor
                        },
                        new CuiElement().Parent = parent,
                        panel
                    }
                };
            }

            public static void Panel(ref CuiElementContainer container, string panel, string color, string min, string max, bool cursor = false)
            {
                container.Add(new CuiPanel
                {
                    Image = { Color = color },
                    RectTransform = { AnchorMin = min, AnchorMax = max },
                    CursorEnabled = cursor
                },
                panel);
            }

            public static void Label(ref CuiElementContainer container, string panel, string color, string text, int size, string min, string max, TextAnchor align = TextAnchor.MiddleCenter)
            {
                container.Add(new CuiLabel
                {
                    Text = { Color = color, FontSize = size, Align = align, Text = text },
                    RectTransform = { AnchorMin = min, AnchorMax = max }
                },
                panel);
            }

            public static void Button(ref CuiElementContainer container, string panel, string color, string text, int size, string min, string max, string command, TextAnchor align = TextAnchor.MiddleCenter)
            {
                container.Add(new CuiButton
                {
                    Button = { Color = color, Command = command, FadeIn = 0f },
                    RectTransform = { AnchorMin = min, AnchorMax = max },
                    Text = { Text = text, FontSize = size, Align = align }
                },
                panel);
            }

            public static void Input(ref CuiElementContainer container, string panel, string color, string text, int size, string min, string max, string command, TextAnchor align = TextAnchor.MiddleCenter)
            {
                container.Add(new CuiElement
                {
                    Name = CuiHelper.GetGuid(),
                    Parent = panel,
                    Components =
                    {
                        new CuiInputFieldComponent
                        {
                            Align = align,
                            CharsLimit = 60,
                            Color = color,
                            Command = command + text,
                            FontSize = size,
                            IsPassword = false,
                            Text = text
                        },
                        new CuiRectTransformComponent { AnchorMin = min, AnchorMax = max },
                        new CuiNeedsCursorComponent()
                    }
                });
            }

            public static void Icon(ref CuiElementContainer container, string panel, string color, string imageurl, string min, string max)
            {
                container.Add(new CuiElement
                {
                    Name = CuiHelper.GetGuid(),
                    Parent = panel,
                    Components =
                    {
                        new CuiRawImageComponent
                        {
                            Url = imageurl,
                            Sprite = "assets/content/textures/generic/fulltransparent.tga",
                            Color = color
                        },
                        new CuiRectTransformComponent
                        {
                            AnchorMin = min,
                            AnchorMax = max
                        }
                    }
                });
            }

            public static string Color(string hexColor, float alpha)
            {
                if (hexColor.StartsWith("#"))
                {
                    hexColor = hexColor.Substring(1);
                }
                int red = int.Parse(hexColor.Substring(0, 2), NumberStyles.AllowHexSpecifier);
                int green = int.Parse(hexColor.Substring(2, 2), NumberStyles.AllowHexSpecifier);
                int blue = int.Parse(hexColor.Substring(4, 2), NumberStyles.AllowHexSpecifier);
                return $"{(double)red / 255} {(double)green / 255} {(double)blue / 255} {alpha}";
            }
        }
        #endregion UICLASS
    }
}
