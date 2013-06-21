﻿/* Copyright 2010-2013 10gen Inc.
*
* Licensed under the Apache License, Version 2.0 (the "License");
* you may not use this file except in compliance with the License.
* You may obtain a copy of the License at
*
* http://www.apache.org/licenses/LICENSE-2.0
*
* Unless required by applicable law or agreed to in writing, software
* distributed under the License is distributed on an "AS IS" BASIS,
* WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
* See the License for the specific language governing permissions and
* limitations under the License.
*/

using System;
using System.Collections.Generic;
using System.IO;
using MongoDB.Bson;
using MongoDB.Bson.IO;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Options;
using MongoDB.Bson.Serialization.Serializers;

namespace MongoDB.Driver.Internal
{
    internal class MongoReplyMessage<TDocument> : MongoMessage
    {
        // private fields
        private readonly BsonBinaryReaderSettings _readerSettings;
        private readonly IBsonSerializer _serializer;
        private ResponseFlags _responseFlags;
        private long _cursorId;
        private int _startingFrom;
        private int _numberReturned;
        private List<TDocument> _documents;

        // constructors
        internal MongoReplyMessage(BsonBinaryReaderSettings readerSettings, IBsonSerializer serializer)
            : base(MessageOpcode.Reply)
        {
            if (readerSettings == null)
            {
                throw new ArgumentNullException("readerSettings");
            }
            if (serializer == null)
            {
                throw new ArgumentNullException("serializer");
            }

            _readerSettings = readerSettings;
            _serializer = serializer;
        }

        // internal properties
        internal ResponseFlags ResponseFlags
        {
            get { return _responseFlags; }
        }

        internal long CursorId
        {
            get { return _cursorId; }
        }

        internal int StartingFrom
        {
            get { return _startingFrom; }
        }

        internal int NumberReturned
        {
            get { return _numberReturned; }
        }

        internal List<TDocument> Documents
        {
            get { return _documents; }
        }

        // internal methods
        internal void ReadFrom(Stream stream, IBsonSerializationOptions serializationOptions)
        {
            if (serializationOptions == null && typeof(TDocument) == typeof(BsonDocument))
            {
                serializationOptions = DocumentSerializationOptions.AllowDuplicateNamesInstance;
            }

            var messageStartPosition = stream.Position;

            var streamReader = new BsonStreamReader(stream, _readerSettings.Encoding);
            ReadMessageHeaderFrom(streamReader);
            _responseFlags = (ResponseFlags)streamReader.ReadInt32();
            _cursorId = streamReader.ReadInt64();
            _startingFrom = streamReader.ReadInt32();
            _numberReturned = streamReader.ReadInt32();

            if ((_responseFlags & ResponseFlags.CursorNotFound) != 0)
            {
                throw new MongoQueryException("Cursor not found.");
            }
            if ((_responseFlags & ResponseFlags.QueryFailure) != 0)
            {
                BsonDocument document;
                using (BsonReader bsonReader = new BsonBinaryReader(stream, _readerSettings))
                {
                    document = (BsonDocument)BsonDocumentSerializer.Instance.Deserialize(bsonReader, typeof(BsonDocument), null);
                }
                var err = document.GetValue("$err", "Unknown error.");
                var message = string.Format("QueryFailure flag was {0} (response was {1}).", err, document.ToJson());
                throw new MongoQueryException(message, document);
            }

            _documents = new List<TDocument>(_numberReturned);
            for (int i = 0; i < _numberReturned; i++)
            {
                using (var bsonReader = new BsonBinaryReader(stream, _readerSettings))
                {
                    var document = (TDocument)_serializer.Deserialize(bsonReader, typeof(TDocument), serializationOptions);
                    _documents.Add(document);
                }
            }
        }
    }
}