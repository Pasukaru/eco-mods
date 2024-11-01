// Copyright (c) Strange Loop Games. All rights reserved.
// See LICENSE file in the project root for full license information.

namespace Eco.Mods.TechTree
{
    using System.ComponentModel;
    using Eco.Core.Controller;
    using Eco.Gameplay.DynamicValues;
    using Eco.Gameplay.GameActions;
    using Eco.Gameplay.Items;
    using Eco.Gameplay.Systems.TextLinks;
    using Eco.Shared.Items;
    using Eco.Shared.Localization;
    using Eco.Shared.Serialization;
    using Eco.World.Blocks;
    using Eco.Gameplay.Objects;
    using Eco.Core.Items;
    using Eco.Gameplay.Players;
    using Eco.Shared.SharedTypes;
    using Eco.Gameplay.Interactions.Interactors;
    using Eco.Shared.Math;
    using System.Linq;

    [Serialized]
    [LocDisplayName("Pickaxe")]
    [Weight(0)]
    [Category("Hidden"), Tag("Excavation")]
    public abstract partial class PickaxeItem : ToolItem, IInteractor
    {
        bool AutoCollect = true; //Enable auto collect?
        int AutoCollectLevel = 7; //At which level its auto collect allowed?
        bool LuckyBreak = true; //Lucky break enabled?
        int LuckyBreakLevel = 3; //at which level is lucky break allowed?
        //if you set level to 0, everyone are allowed to use it.

        private static SkillModifiedValue caloriesBurn = CreateCalorieValue(20, typeof(MiningSkill), typeof(PickaxeItem));
        static PickaxeItem() { }

        public override IDynamicValue CaloriesBurn                  => caloriesBurn;


        public override              Item          RepairItem       => Item.Get<StoneItem>();
        [SyncToView] public override IDynamicValue Damage           => base.Damage;      // damage per tier level
        [SyncToView] public override IDynamicValue Tier             => base.Tier; // tool tier, overriden to have SyncToView only for pickaxe

        [SyncToView] public override IDynamicValue PerkDamage       => base.PerkDamage;
        public override              int           FullRepairAmount => 1;

        //Todo, integrate into interactor system
        public override bool CanPickUpItemStack(ItemStack stack)
        {
            var blockitem = stack.Item as BlockItem;
            return stack.Item.IsCarried && blockitem != null && Block.Get<Minable>(blockitem.OriginType) != null;
        }

        [Interaction(InteractionTrigger.LeftClick, tags: BlockTags.Minable, AnimationDriven = true)]
        [Interaction(InteractionTrigger.LeftClick, tags: BlockTags.MinableRubble, highlightColorHex: InteractionHexColors.Yellow, AnimationDriven = true)] //Specific for big rubble so it can be highlighted yellow.
        public bool Mine(Player player, InteractionTriggerInfo triggerInfo, InteractionTarget target)
        {
            //Try to get any minable blocks we are targeting
            if (this.TryCreateMultiblockContext(out var context, target, player, tagsTargetable: new[] { BlockTags.Minable,BlockTags.MinableRubble }, applyXPSkill: false))
            {
                var user = player.User;
                using var pack = new GameActionPack();

                foreach ((var block, var pos) in context.Area.Select(pos => (World.World.GetBlock(pos), pos))) //Get blocks from positions
                {
                    //Calculate damage dealt to the block
                    var totalDamageToTarget = user.BlockHitCache.MemorizeHit(block.GetType(), target.BlockPosition.Value, this.PerkDamage.GetCurrentValue(player!.User) + this.Damage.GetCurrentValue(player!.User));

                    //Check if enough damage was dealt to destroy the block
                    if (block.Get<Minable>().Hardness <= totalDamageToTarget)
                    {
                        //Delete the block and spawn rubble
                        pack.DeleteBlock(this.CreateMultiblockContext(player, false, pos), spawnRubble: false);
                        pack.AddPostEffect(() =>
                        {
                            var item = block is IRepresentsItem ? Item.Get((IRepresentsItem)block) : null;
                            var addition = item != null ? " " + item.UILink() : string.Empty;
                            var miningSkill = user.Skillset.GetSkill(typeof(MiningSkill));

                            bool pickupItems = false;
                            if (AutoCollect) {
                                pickupItems = (miningSkill.Level >= AutoCollectLevel) ? user.Inventory.Carried.TryAddItemsNonUnique(item.GetType(), RubbleObject.MaxAmountPerBlock).Success : false;
                            }
                            if(pickupItems)
                            {
                                this.AddExperience(user, 1f, new LocString(Localizer.Format("mining") + addition));
                                user.BlockHitCache.ForgetHit(target.BlockPosition.Value);
                            }
                            else
                            {
                                var forced = (miningSkill.Level >= LuckyBreakLevel && LuckyBreak) ? RubbleObject.MaxAmountPerBlock : -1;
                                if (RubbleObject.TrySpawnFromBlock(player, block.GetType(), target.BlockPosition.Value, forced))
                                {
                                    this.AddExperience(user, 1f, new LocString(Localizer.Format("mining") + addition)); //Add experience based on the tool's experience rate (altered in the EcoTechTree.csv)
                                    user.UserUI.OnCreateRubble.Invoke(item.DisplayName.NotTranslated);
                                    user.BlockHitCache.ForgetHit(target.BlockPosition.Value);
                                }
                            }
                        });
                    }
                    else pack.UseTool(this.CreateMultiblockContext(player, false, pos)); //Use tool to damage the block
                }

                return pack.TryPerform(user).Success;
            }
            else if (target.NetObj is RubbleObject rubble && rubble.IsBreakable)
            {
               using var pack = new GameActionPack();
               pack.UseTool(this.CreateMultiblockContext(player, false, rubble.Position.Round())); //Break the rubble, but do not trigger an AddExperience call
               if (pack.EarlyResult) pack.AddPostEffect(() => rubble.Breakup(player));
               if (pack.EarlyResult) return pack.TryPerform(null).Success;
            }

            return false;
        }
    }
}
