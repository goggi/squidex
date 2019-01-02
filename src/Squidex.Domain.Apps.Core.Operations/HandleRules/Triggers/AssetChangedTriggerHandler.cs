﻿// ==========================================================================
//  Squidex Headless CMS
// ==========================================================================
//  Copyright (c) Squidex UG (haftungsbeschränkt)
//  All rights reserved. Licensed under the MIT license.
// ==========================================================================

using Squidex.Domain.Apps.Core.HandleRules.EnrichedEvents;
using Squidex.Domain.Apps.Core.Rules.Triggers;
using Squidex.Domain.Apps.Core.Scripting;
using Squidex.Infrastructure;

namespace Squidex.Domain.Apps.Core.HandleRules.Triggers
{
    public sealed class AssetChangedTriggerHandler : RuleTriggerHandler<AssetChangedTriggerV2>
    {
        private readonly IScriptEngine scriptEngine;

        public AssetChangedTriggerHandler(IScriptEngine scriptEngine)
        {
            Guard.NotNull(scriptEngine, nameof(scriptEngine));

            this.scriptEngine = scriptEngine;
        }

        protected override bool Triggers(EnrichedEvent @event, AssetChangedTriggerV2 trigger)
        {
            return @event is EnrichedAssetEvent assetEvent && MatchsType(trigger, assetEvent);
        }

        private bool MatchsType(AssetChangedTriggerV2 trigger, EnrichedAssetEvent assetEvent)
        {
            return string.IsNullOrWhiteSpace(trigger.Condition) || scriptEngine.Evaluate("event", assetEvent, trigger.Condition);
        }
    }
}
