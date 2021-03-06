﻿// ==========================================================================
//  Squidex Headless CMS
// ==========================================================================
//  Copyright (c) Squidex UG (haftungsbeschraenkt)
//  All rights reserved. Licensed under the MIT license.
// ==========================================================================

using System;
using System.Collections.Generic;
using System.Security.Claims;
using FakeItEasy;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using NodaTime;
using Squidex.Domain.Apps.Core.Assets;
using Squidex.Domain.Apps.Core.Contents;
using Squidex.Domain.Apps.Core.HandleRules;
using Squidex.Domain.Apps.Core.HandleRules.Scripting;
using Squidex.Domain.Apps.Core.Rules.EnrichedEvents;
using Squidex.Domain.Apps.Core.Scripting;
using Squidex.Domain.Apps.Core.Scripting.Extensions;
using Squidex.Infrastructure;
using Squidex.Infrastructure.Json.Objects;
using Squidex.Shared.Identity;
using Squidex.Shared.Users;
using Xunit;

namespace Squidex.Domain.Apps.Core.Operations.HandleRules
{
    public class RuleEventFormatterTests
    {
        private readonly IUser user = A.Fake<IUser>();
        private readonly IUrlGenerator urlGenerator = A.Fake<IUrlGenerator>();
        private readonly NamedId<Guid> appId = NamedId.Of(Guid.NewGuid(), "my-app");
        private readonly NamedId<Guid> schemaId = NamedId.Of(Guid.NewGuid(), "my-schema");
        private readonly Instant now = SystemClock.Instance.GetCurrentInstant();
        private readonly Guid contentId = Guid.NewGuid();
        private readonly Guid assetId = Guid.NewGuid();
        private readonly RuleEventFormatter sut;

        public RuleEventFormatterTests()
        {
            A.CallTo(() => urlGenerator.ContentUI(appId, schemaId, contentId))
                .Returns("content-url");

            A.CallTo(() => urlGenerator.AssetContent(assetId))
                .Returns("asset-content-url");

            A.CallTo(() => user.Id)
                .Returns("user123");

            A.CallTo(() => user.Email)
                .Returns("me@email.com");

            A.CallTo(() => user.Claims)
                .Returns(new List<Claim> { new Claim(SquidexClaimTypes.DisplayName, "me") });

            var extensions = new IScriptExtension[]
            {
                new DateTimeScriptExtension(),
                new EventScriptExtension(urlGenerator),
                new StringScriptExtension()
            };

            var cache = new MemoryCache(Options.Create(new MemoryCacheOptions()));

            sut = new RuleEventFormatter(TestUtils.DefaultSerializer, urlGenerator, new JintScriptEngine(cache, extensions));
        }

        [Fact]
        public void Should_serialize_object_to_json()
        {
            var result = sut.ToPayload(new { Value = 1 });

            Assert.NotNull(result);
        }

        [Fact]
        public void Should_create_payload()
        {
            var @event = new EnrichedContentEvent { AppId = appId };

            var result = sut.ToPayload(@event);

            Assert.NotNull(result);
        }

        [Fact]
        public void Should_create_envelope_data_from_event()
        {
            var @event = new EnrichedContentEvent { AppId = appId, Name = "MyEventName" };

            var result = sut.ToEnvelope(@event);

            Assert.Contains("MyEventName", result);
        }

        [Theory]
        [InlineData("Name $APP_NAME has id $APP_ID")]
        [InlineData("Name ${$EVENT_APPID.NAME} has id ${EVENT_APPID.ID}")]
        [InlineData("Script(`Name ${event.appId.name} has id ${event.appId.id}`)")]
        public void Should_format_app_information_from_event(string script)
        {
            var @event = new EnrichedContentEvent { AppId = appId };

            var result = sut.Format(script, @event);

            Assert.Equal($"Name my-app has id {appId.Id}", result);
        }

        [Theory]
        [InlineData("Name $SCHEMA_NAME has id $SCHEMA_ID")]
        [InlineData("Script(`Name ${event.schemaId.name} has id ${event.schemaId.id}`)")]
        public void Should_format_schema_information_from_event(string script)
        {
            var @event = new EnrichedContentEvent { SchemaId = schemaId };

            var result = sut.Format(script, @event);

            Assert.Equal($"Name my-schema has id {schemaId.Id}", result);
        }

        [Theory]
        [InlineData("Full: $TIMESTAMP_DATETIME")]
        [InlineData("Script(`Full: ${formatDate(event.timestamp, 'yyyy-MM-dd-hh-mm-ss')}`)")]
        public void Should_format_timestamp_information_from_event(string script)
        {
            var @event = new EnrichedContentEvent { Timestamp = now };

            var result = sut.Format(script, @event);

            Assert.Equal($"Full: {now:yyyy-MM-dd-hh-mm-ss}", result);
        }

        [Theory]
        [InlineData("Date: $TIMESTAMP_DATE")]
        [InlineData("Script(`Date: ${formatDate(event.timestamp, 'yyyy-MM-dd')}`)")]
        public void Should_format_timestamp_date_information_from_event(string script)
        {
            var @event = new EnrichedContentEvent { Timestamp = now };

            var result = sut.Format(script, @event);

            Assert.Equal($"Date: {now:yyyy-MM-dd}", result);
        }

        [Theory]
        [InlineData("From $MENTIONED_NAME ($MENTIONED_EMAIL, $MENTIONED_ID)")]
        [InlineData("From ${COMMENT_MENTIONEDUSER.NAME} (${COMMENT_MENTIONEDUSER.EMAIL}, ${COMMENT_MENTIONEDUSER.ID})")]
        [InlineData("Script(`From ${event.mentionedUser.name} (${event.mentionedUser.email}, ${event.mentionedUser.id})`)")]
        public void Should_format_email_and_display_name_from_mentioned_user(string script)
        {
            var @event = new EnrichedCommentEvent { MentionedUser = user };

            var result = sut.Format(script, @event);

            Assert.Equal("From me (me@email.com, user123)", result);
        }

        [Theory]
        [InlineData("From $USER_NAME ($USER_EMAIL, $USER_ID)")]
        [InlineData("Script(`From ${event.user.name} (${event.user.email}, ${event.user.id})`)")]
        public void Should_format_email_and_display_name_from_user(string script)
        {
            var @event = new EnrichedContentEvent { User = user };

            var result = sut.Format(script, @event);

            Assert.Equal("From me (me@email.com, user123)", result);
        }

        [Theory]
        [InlineData("From $USER_NAME ($USER_EMAIL, $USER_ID)")]
        [InlineData("Script(`From ${event.user.name} (${event.user.email}, ${event.user.id})`)")]
        public void Should_return_null_if_user_is_not_found(string script)
        {
            var @event = new EnrichedContentEvent();

            var result = sut.Format(script, @event);

            Assert.Equal("From null (null, null)", result);
        }

        [Theory]
        [InlineData("From $USER_NAME ($USER_EMAIL, $USER_ID)")]
        [InlineData("Script(`From ${event.user.name} (${event.user.email}, ${event.user.id})`)")]
        public void Should_format_email_and_display_name_from_client(string script)
        {
            var @event = new EnrichedContentEvent { User = new ClientUser(new RefToken(RefTokenType.Client, "android")) };

            var result = sut.Format(script, @event);

            Assert.Equal("From client:android (client:android, android)", result);
        }

        [Theory]
        [InlineData("Version: $ASSET_VERSION")]
        [InlineData("Script(`Version: ${event.version}`)")]
        public void Should_format_base_property(string script)
        {
            var @event = new EnrichedAssetEvent { Version = 13 };

            var result = sut.Format(script, @event);

            Assert.Equal("Version: 13", result);
        }

        [Theory]
        [InlineData("File: $ASSET_FILENAME")]
        [InlineData("Script(`File: ${event.fileName}`)")]
        public void Should_format_asset_file_name_from_event(string script)
        {
            var @event = new EnrichedAssetEvent { FileName = "my-file.png" };

            var result = sut.Format(script, @event);

            Assert.Equal("File: my-file.png", result);
        }

        [Theory]
        [InlineData("Type: $ASSET_ASSETTYPE")]
        [InlineData("Script(`Type: ${event.assetType}`)")]
        public void Should_format_asset_asset_type_from_event(string script)
        {
            var @event = new EnrichedAssetEvent { AssetType = AssetType.Audio };

            var result = sut.Format(script, @event);

            Assert.Equal("Type: Audio", result);
        }

        [Theory]
        [InlineData("Download at $ASSET_CONTENT_URL")]
        [InlineData("Script(`Download at ${assetContentUrl()}`)")]
        public void Should_format_asset_content_url_from_event(string script)
        {
            var @event = new EnrichedAssetEvent { Id = assetId };

            var result = sut.Format(script, @event);

            Assert.Equal("Download at asset-content-url", result);
        }

        [Theory]
        [InlineData("Download at $ASSET_CONTENT_URL")]
        [InlineData("Script(`Download at ${assetContentUrl()}`)")]
        public void Should_return_null_when_asset_content_url_not_found(string script)
        {
            var @event = new EnrichedContentEvent();

            var result = sut.Format(script, @event);

            Assert.Equal("Download at null", result);
        }

        [Theory]
        [InlineData("Go to $CONTENT_URL")]
        [InlineData("Script(`Go to ${contentUrl()}`)")]
        public void Should_format_content_url_from_event(string script)
        {
            var @event = new EnrichedContentEvent { AppId = appId, Id = contentId, SchemaId = schemaId };

            var result = sut.Format(script, @event);

            Assert.Equal("Go to content-url", result);
        }

        [Theory]
        [InlineData("Go to $CONTENT_URL")]
        [InlineData("Script(`Go to ${contentUrl()}`)")]
        public void Should_return_null_when_content_url_when_not_found(string script)
        {
            var @event = new EnrichedAssetEvent();

            var result = sut.Format(script, @event);

            Assert.Equal("Go to null", result);
        }

        [Theory]
        [InlineData("$CONTENT_STATUS")]
        [InlineData("Script(contentAction())")]
        [InlineData("Script(`${event.status}`)")]
        public void Should_format_content_status_when_found(string script)
        {
            var @event = new EnrichedContentEvent { Status = Status.Published };

            var result = sut.Format(script, @event);

            Assert.Equal("Published", result);
        }

        [Theory]
        [InlineData("$CONTENT_ACTION")]
        [InlineData("Script(contentAction())")]
        public void Should_return_null_when_content_status_not_found(string script)
        {
            var @event = new EnrichedAssetEvent();

            var result = sut.Format(script, @event);

            Assert.Equal("null", result);
        }

        [Theory]
        [InlineData("$CONTENT_ACTION")]
        [InlineData("Script(`${event.type}`)")]
        public void Should_format_content_actions_when_found(string script)
        {
            var @event = new EnrichedContentEvent { Type = EnrichedContentEventType.Created };

            var result = sut.Format(script, @event);

            Assert.Equal("Created", result);
        }

        [Theory]
        [InlineData("$CONTENT_ACTION")]
        [InlineData("Script(contentAction())")]
        public void Should_return_null_when_content_action_not_found(string script)
        {
            var @event = new EnrichedAssetEvent();

            var result = sut.Format(script, @event);

            Assert.Equal("null", result);
        }

        [Theory]
        [InlineData("$CONTENT_DATA.country.iv")]
        [InlineData("Script(`${event.data.country.iv}`)")]
        public void Should_return_null_when_field_not_found(string script)
        {
            var @event = new EnrichedContentEvent
            {
                Data =
                    new NamedContentData()
                        .AddField("city",
                            new ContentFieldData()
                                .AddValue("iv", "Berlin"))
            };

            var result = sut.Format(script, @event);

            Assert.Equal("null", result);
        }

        [Theory]
        [InlineData("$CONTENT_DATA.city.de")]
        [InlineData("Script(`${event.data.country.iv}`)")]
        public void Should_return_null_when_partition_not_found(string script)
        {
            var @event = new EnrichedContentEvent
            {
                Data =
                    new NamedContentData()
                        .AddField("city",
                            new ContentFieldData()
                                .AddValue("iv", "Berlin"))
            };

            var result = sut.Format(script, @event);

            Assert.Equal("null", result);
        }

        [Theory]
        [InlineData("$CONTENT_DATA.city.iv.10")]
        [InlineData("Script(`${event.data.country.de[10]}`)")]
        public void Should_return_null_when_array_item_not_found(string script)
        {
            var @event = new EnrichedContentEvent
            {
                Data =
                    new NamedContentData()
                        .AddField("city",
                            new ContentFieldData()
                                .AddJsonValue(JsonValue.Array()))
            };

            var result = sut.Format(script, @event);

            Assert.Equal("null", result);
        }

        [Theory]
        [InlineData("$CONTENT_DATA.city.de.Name")]
        [InlineData("Script(`${event.data.city.de.Location}`)")]
        public void Should_return_null_when_property_not_found(string script)
        {
            var @event = new EnrichedContentEvent
            {
                Data =
                    new NamedContentData()
                        .AddField("city",
                            new ContentFieldData()
                                .AddJsonValue(JsonValue.Object().Add("name", "Berlin")))
            };

            var result = sut.Format(script, @event);

            Assert.Equal("null", result);
        }

        [Theory]
        [InlineData("$CONTENT_DATA.city.iv")]
        [InlineData("Script(`${event.data.city.iv}`)")]
        public void Should_return_plain_value_when_found(string script)
        {
            var @event = new EnrichedContentEvent
            {
                Data =
                    new NamedContentData()
                        .AddField("city",
                            new ContentFieldData()
                                .AddValue("iv", "Berlin"))
            };

            var result = sut.Format(script, @event);

            Assert.Equal("Berlin", result);
        }

        [Theory]
        [InlineData("$CONTENT_DATA.city.iv.0")]
        [InlineData("Script(`${event.data.city.iv[0]}`)")]
        public void Should_return_plain_value_from_array_when_found(string script)
        {
            var @event = new EnrichedContentEvent
            {
                Data =
                    new NamedContentData()
                        .AddField("city",
                            new ContentFieldData()
                                .AddJsonValue(JsonValue.Array("Berlin")))
            };

            var result = sut.Format(script, @event);

            Assert.Equal("Berlin", result);
        }

        [Theory]
        [InlineData("$CONTENT_DATA.city.iv.name")]
        [InlineData("Script(`${event.data.city.iv.name}`)")]
        public void Should_return_plain_value_from_object_when_found(string script)
        {
            var @event = new EnrichedContentEvent
            {
                Data =
                    new NamedContentData()
                        .AddField("city",
                            new ContentFieldData()
                                .AddJsonValue(JsonValue.Object().Add("name", "Berlin")))
            };

            var result = sut.Format(script, @event);

            Assert.Equal("Berlin", result);
        }

        [Theory]
        [InlineData("$CONTENT_DATA.city.iv")]
        [InlineData("Script(`${JSON.stringify(event.data.city.iv)}`)")]
        public void Should_return_json_string_when_object(string script)
        {
            var @event = new EnrichedContentEvent
            {
                Data =
                    new NamedContentData()
                        .AddField("city",
                            new ContentFieldData()
                                .AddJsonValue(JsonValue.Object().Add("name", "Berlin")))
            };

            var result = sut.Format(script, @event);

            Assert.Equal("{\"name\":\"Berlin\"}", result);
        }

        [Theory]
        [InlineData("Script(`From ${event.actor}`)")]
        public void Should_format_actor(string script)
        {
            var @event = new EnrichedContentEvent { Actor = new RefToken(RefTokenType.Client, "android") };

            var result = sut.Format(script, @event);

            Assert.Equal("From client:android", result);
        }

        [Fact]
        public void Should_format_json()
        {
            var @event = new EnrichedContentEvent { Actor = new RefToken(RefTokenType.Client, "android") };

            var result = sut.Format("Script(JSON.stringify({ actor: event.actor.toString() }))", @event);

            Assert.Equal("{\"actor\":\"client:android\"}", result);
        }

        [Fact]
        public void Should_format_json_with_special_characters()
        {
            var @event = new EnrichedContentEvent { Actor = new RefToken(RefTokenType.Client, "mobile\"android") };

            var result = sut.Format("Script(JSON.stringify({ actor: event.actor.toString() }))", @event);

            Assert.Equal("{\"actor\":\"client:mobile\\\"android\"}", result);
        }

        [Fact]
        public void Should_evaluate_script_if_starting_with_whitespace()
        {
            var @event = new EnrichedContentEvent { Type = EnrichedContentEventType.Created };

            var result = sut.Format(" Script(`${event.type}`)", @event);

            Assert.Equal("Created", result);
        }

        [Fact]
        public void Should_evaluate_script_if_ends_with_whitespace()
        {
            var @event = new EnrichedContentEvent { Type = EnrichedContentEventType.Created };

            var result = sut.Format("Script(`${event.type}`) ", @event);

            Assert.Equal("Created", result);
        }
    }
}
