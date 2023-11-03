﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using EFT;
using SPTQuestingBots.BehaviorExtensions;
using SPTQuestingBots.BotLogic.HiveMind;
using SPTQuestingBots.Controllers;

namespace SPTQuestingBots.BotLogic.Follow
{
    internal class BotFollowerLayer : CustomLayerDelayedUpdate
    {
        private Objective.BotObjectiveManager objectiveManager;
        private double searchTimeAfterCombat = ConfigController.Config.SearchTimeAfterCombat.Min;
        private double maxDistanceFromBoss = ConfigController.Config.BotQuestingRequirements.MaxFollowerDistance.TargetRange.Min;
        private bool wasAbleBodied = true;

        public BotFollowerLayer(BotOwner _botOwner, int _priority) : base(_botOwner, _priority, 25)
        {
            objectiveManager = BotOwner.GetPlayer.gameObject.GetOrAddComponent<Objective.BotObjectiveManager>();
        }

        public override string GetName()
        {
            return "BotFollowerLayer";
        }

        public override Action GetNextAction()
        {
            return base.GetNextAction();
        }

        public override bool IsCurrentActionEnding()
        {
            return base.IsCurrentActionEnding();
        }

        public override bool IsActive()
        {
            if (!canUpdate() && QuestingBotsPluginConfig.QuestingLogicTimeGatingEnabled.Value)
            {
                return previousState;
            }

            // Check if somebody disabled questing in the F12 menu
            if (!QuestingBotsPluginConfig.QuestingEnabled.Value)
            {
                return updatePreviousState(false);
            }

            if (BotOwner.BotState != EBotState.Active)
            {
                return updatePreviousState(false);
            }

            // If the layer is active, run to the boss. Otherwise, allow a little more space
            if (previousState)
            {
                maxDistanceFromBoss = ConfigController.Config.BotQuestingRequirements.MaxFollowerDistance.TargetRange.Min;
            }
            else
            {
                maxDistanceFromBoss = ConfigController.Config.BotQuestingRequirements.MaxFollowerDistance.TargetRange.Max;
            }

            // Only use this layer if the bot has a boss to follow and the boss can quest
            if (!BotHiveMindMonitor.HasBoss(BotOwner) || !BotHiveMindMonitor.GetValueForBossOfBot(BotHiveMindSensorType.CanQuest, BotOwner))
            {
                return updatePreviousState(false);
            }

            // Only enable the layer if the bot is too far from the boss
            float? distanceToBoss = BotHiveMindMonitor.GetDistanceToBoss(BotOwner);
            if (!distanceToBoss.HasValue || (distanceToBoss.Value < maxDistanceFromBoss))
            {
                return updatePreviousState(false);
            }

            // Prevent the bot from following its boss if it needs to heal, etc. 
            if (!objectiveManager.BotMonitor.IsAbleBodied(wasAbleBodied))
            {
                wasAbleBodied = false;
                return updatePreviousState(false);
            }
            if (!wasAbleBodied)
            {
                LoggingController.LogInfo("Bot " + BotOwner.Profile.Nickname + " is now able-bodied.");
            }
            wasAbleBodied = true;

            // Prevent the bot from following its boss if it's in combat
            if (objectiveManager.BotMonitor.ShouldSearchForEnemy(searchTimeAfterCombat))
            {
                if (!BotHiveMindMonitor.GetValueForBot(BotHiveMindSensorType.InCombat, BotOwner))
                {
                    searchTimeAfterCombat = objectiveManager.BotMonitor.UpdateSearchTimeAfterCombat();
                    //LoggingController.LogInfo("Bot " + BotOwner.Profile.Nickname + " will spend " + searchTimeAfterCombat + " seconds searching for enemies after combat ends..");
                }
                BotHiveMindMonitor.UpdateValueForBot(BotHiveMindSensorType.InCombat, BotOwner, true);
                return updatePreviousState(false);
            }
            BotHiveMindMonitor.UpdateValueForBot(BotHiveMindSensorType.InCombat, BotOwner, false);

            // If any group members are in combat, the bot should also be in combat
            if (BotHiveMindMonitor.GetValueForGroup(BotHiveMindSensorType.InCombat, BotOwner))
            {
                // WIP. Hopefully not needed with SAIN.
                //BotHiveMindMonitor.AssignTargetEnemyFromGroup(BotOwner);

                //IReadOnlyCollection<BotOwner> groupMembers = BotHiveMindMonitor.GetAllGroupMembers(BotOwner);
                //LoggingController.LogInfo("One of the following group members is in combat: " + string.Join(", ", groupMembers.Select(g => g.Profile.Nickname)));

                return updatePreviousState(false);
            }

            // Check if enough time has elapsed since its boss last looted
            TimeSpan timeSinceBossLastLooted = DateTime.Now - BotHiveMindMonitor.GetLastLootingTimeForBoss(BotOwner);
            bool bossWillAllowLooting = timeSinceBossLastLooted.TotalSeconds > ConfigController.Config.BotQuestingRequirements.BreakForLooting.MinTimeBetweenFollowerLootingChecks;

            // Don't allow the follower to wander too far from the boss when it's looting
            bool tooFarFromBossForLooting = BotHiveMindMonitor.GetDistanceToBoss(BotOwner) > ConfigController.Config.BotQuestingRequirements.BreakForLooting.MaxDistanceFromBoss;

            // Only allow looting if the bot is already looting or its boss will allow it
            if
            (
                (bossWillAllowLooting && objectiveManager.BotMonitor.ShouldCheckForLoot(objectiveManager.BotMonitor.NextLootCheckDelay))
                || (!tooFarFromBossForLooting && objectiveManager.BotMonitor.IsLooting())
            )
            {
                BotHiveMindMonitor.UpdateValueForBot(BotHiveMindSensorType.WantsToLoot, BotOwner, true);
                return updatePreviousState(pauseLayer(ConfigController.Config.BotQuestingRequirements.BreakForLooting.MaxTimeToStartLooting));
            }
            BotHiveMindMonitor.UpdateValueForBot(BotHiveMindSensorType.WantsToLoot, BotOwner, false);

            setNextAction(BotActionType.FollowBoss, "FollowBoss");
            return updatePreviousState(true);
        }
    }
}
