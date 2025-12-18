using ECommons.Automation;
using ECommons.DalamudServices;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Lumina.Excel.Sheets;
using PandorasBox.FeaturesSetup;
using PandorasBox.Helpers;
using System.Collections.Generic;
using System.Linq;

namespace PandorasBox.Features.UI
{
    public unsafe class FCChestMergeStacks : Feature
    {
        public override string Name => "Automatically merge FC Chest stacks";

        public override string Description => "When you open the FC Chest, the plugin will try and pull all stacks of the same item together.";

        public override FeatureType FeatureType => FeatureType.UI;

        public override bool FeatureDisabled => false;

        public override string DisabledReason => "Issues with crashing";

        public List<InventorySlot> fcChestSlots = new();

        private bool FCChestOpened { get; set; } = false;

        private Dictionary<uint, Item> Sheet { get; set; }

        public class InventorySlot
        {
            public InventoryType Container { get; set; }

            public short Slot { get; set; }

            public uint ItemId { get; set; }

            public bool ItemHQ { get; set; }
        }

        public class Configs : FeatureConfig
        {
            [FeatureConfigOption("Merge across all pages")]
            public bool MergeAcrossPages = false;

            [FeatureConfigOption("Sort after merging")]
            public bool SortAfter = false;
        }

        public Configs Config { get; private set; }

        public override bool UseAutoConfig => true;

        public override void Enable()
        {
            Sheet = Svc.Data.GetExcelSheet<Item>().ToDictionary(x => x.RowId, x => x);
            Config = LoadConfig<Configs>() ?? new Configs();
            Svc.Framework.Update += RunFeature;
            base.Enable();
        }

        private void RunFeature(IFramework framework)
        {
            if (Svc.Condition[Dalamud.Game.ClientState.Conditions.ConditionFlag.Occupied | Dalamud.Game.ClientState.Conditions.ConditionFlag.Casting])
            {
                TaskManager.Abort();
                return;
            }

            if (!GenericHelpers.TryGetAddonByName<AtkUnitBase>("FreeCompanyChest", out var addon))
            {
                FCChestOpened = false;
                return;
            }

            if (addon == null || !addon->IsVisible)
            {
                FCChestOpened = false;
                return;
            }

            // Check if deposit or withdraw windows are open
            if (addon->UldManager.NodeListCount > 4 && addon->UldManager.NodeList[4]->IsVisible())
            {
                return;
            }
            if (addon->UldManager.NodeListCount > 7 && addon->UldManager.NodeList[7]->IsVisible())
            {
                return;
            }

            if (!FCChestOpened)
            {
                FCChestOpened = true;
                fcChestSlots.Clear();
                var inv = InventoryManager.Instance();

                // Process all FC Chest pages
                var fcPages = new[]
                {
                    InventoryType.FreeCompanyPage1,
                    InventoryType.FreeCompanyPage2,
                    InventoryType.FreeCompanyPage3,
                    InventoryType.FreeCompanyPage4,
                    InventoryType.FreeCompanyPage5
                };

                foreach (var fcPage in fcPages)
                {
                    var container = inv->GetInventoryContainer(fcPage);
                    if (container == null) continue;

                    for (var i = 0; i < container->Size; i++)
                    {
                        var item = container->GetInventorySlot(i);
                        if (item->ItemId == 0) continue;
                        if (item->Flags.HasFlag(InventoryItem.ItemFlags.Collectable)) continue;
                        if (!Sheet.ContainsKey(item->ItemId)) continue;
                        if (item->Quantity == Sheet[item->ItemId].StackSize) continue;

                        var slot = new InventorySlot
                        {
                            Container = fcPage,
                            ItemId = item->ItemId,
                            Slot = item->Slot,
                            ItemHQ = item->Flags.HasFlag(InventoryItem.ItemFlags.HighQuality)
                        };
                        fcChestSlots.Add(slot);
                    }
                }

                if (Config.MergeAcrossPages)
                {
                    // Merge across all pages
                    foreach (var item in fcChestSlots.GroupBy(x => new { x.ItemId, x.ItemHQ }).Where(x => x.Count() > 1))
                    {
                        var firstSlot = item.First();
                        for (var i = 1; i < item.Count(); i++)
                        {
                            var slot = item.ToList()[i];
                            inv->MoveItemSlot(slot.Container, (ushort)slot.Slot, firstSlot.Container, (ushort)firstSlot.Slot, true);
                        }
                    }
                }
                else
                {
                    // Merge within each page separately
                    foreach (var page in fcPages)
                    {
                        var pageSlots = fcChestSlots.Where(x => x.Container == page).ToList();
                        foreach (var item in pageSlots.GroupBy(x => new { x.ItemId, x.ItemHQ }).Where(x => x.Count() > 1))
                        {
                            var firstSlot = item.First();
                            for (var i = 1; i < item.Count(); i++)
                            {
                                var slot = item.ToList()[i];
                                inv->MoveItemSlot(slot.Container, (ushort)slot.Slot, firstSlot.Container, (ushort)firstSlot.Slot, true);
                            }
                        }
                    }
                }

                if (fcChestSlots.GroupBy(x => new { x.ItemId, x.ItemHQ }).Any(x => x.Count() > 1) && Config.SortAfter)
                {
                    TaskManager.EnqueueDelay(100);
                    TaskManager.Enqueue(() => Chat.SendMessage("/isort condition inventory id"));
                    TaskManager.Enqueue(() => Chat.SendMessage("/isort execute inventory"));
                }
            }
        }

        public override void Disable()
        {
            SaveConfig(Config);
            Sheet = null;
            Svc.Framework.Update -= RunFeature;
            base.Disable();
        }
    }
}

