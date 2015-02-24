﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

namespace LiteDB
{
    public partial class BsonMapper
    {
        /// <summary>
        /// Serialize a POCO class to BsonDocument
        /// </summary>
        public BsonDocument ToDocument(object poco)
        {
            if (poco == null) throw new ArgumentNullException("obj");

            // if object is BsonDocument, just return them
            if (poco is BsonDocument) return (BsonDocument)poco;

            return this.Serialize(poco, 0).AsDocument;
        }

        internal BsonValue Serialize(object obj, int depth)
        {
            if (++depth > MAX_DEPTH) throw new LiteException("Serialization class reach MAX_DEPTH - Check for circular references");

            if (obj == null) return BsonValue.Null;

            var type = obj.GetType();

            // if is already a bson value
            if (obj is BsonValue) return new BsonValue((BsonValue)obj);

            // basic Bson data types (cast datatype for better performance optimization)
            else if (obj is String) return new BsonValue((String)obj);
            else if (obj is Int32) return new BsonValue((Int32)obj);
            else if (obj is Int64) return new BsonValue((Int64)obj);
            else if (obj is Double) return new BsonValue((Double)obj);
            else if (obj is Boolean) return new BsonValue((Boolean)obj);
            else if (obj is Byte[]) return new BsonValue((Byte[])obj);
            else if (obj is DateTime) return new BsonValue((DateTime)obj);
            else if (obj is Guid) return new BsonValue((Guid)obj);
            // basic .net type to convert to bson
            else if (obj is Int16 || obj is UInt16 || obj is Byte)
            {
                return new BsonValue(Convert.ToInt32(obj));
            }
            else if (obj is UInt32 || obj is UInt64)
            {
                return new BsonValue(Convert.ToInt64(obj));
            }
            else if (obj is Single || obj is Decimal)
            {
                return new BsonValue(Convert.ToDouble(obj));
            }
            else if (obj is Char || obj is Enum)
            {
                return new BsonValue(obj.ToString());
            }
            // check if is a list or array
            else if (obj is IList || type.IsArray)
            {
                return this.SerializeArray(obj as IEnumerable, depth);
            }
            // for dictionary
            else if (obj is IDictionary)
            {
                return this.SerializeDictionary(obj as IDictionary, depth);
            }
            // otherwise treat as a plain object
            else
            {
                return this.SerializeObject(type, obj, depth);
            }
        }

        private BsonArray SerializeArray(IEnumerable array, int depth)
        {
            var arr = new BsonArray();

            foreach (var item in array)
            {
                arr.Add(this.Serialize(item, depth));
            }

            return arr;
        }

        private BsonDocument SerializeDictionary(IDictionary dict, int depth)
        {
            var o = new BsonDocument();

            foreach (var key in dict.Keys)
            {
                var value = dict[key];
                o.RawValue[key.ToString()] = this.Serialize(value, depth);
            }

            return o;
        }

        private BsonDocument SerializeObject(Type type, object obj, int depth)
        {
            var o = new BsonDocument();
            var mapper = this.GetPropertyMapper(type);

            foreach (var prop in mapper.Values)
            {
                // get property value 
                var value = prop.Getter(obj);

                if (value == null && this.SerializeNullValues == false) continue;

                o.RawValue[prop.FieldName] = this.Serialize(value, depth);
            }

            return o;
        }
    }
}
