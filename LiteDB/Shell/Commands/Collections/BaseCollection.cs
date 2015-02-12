﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace LiteDB.Shell.Commands
{
    internal class BaseCollection
    {
        /// <summary>
        /// Read collection name from db.(colname).(command)
        /// </summary>
        public LiteCollection<BsonDocument> ReadCollection(LiteDatabase db, StringScanner s)
        {
            return db.GetCollection(s.Scan(@"db\.(\w+)\.\w+\s*", 1));
        }

        public bool IsCollectionCommand(StringScanner s, string command)
        {
            return s.Match(@"db\.\w+\." + command);
        }

        public IEnumerable<BsonDocument> ReadSkipLimit(StringScanner s, IEnumerable<BsonDocument> docs)
        {
            if (s.Match(@"skip\s+\d+"))
            {
                docs = docs.Skip(Convert.ToInt32(s.Scan(@"skip\s+(\d+)\s*", 1)));
            }

            if (s.Match(@"limit\s+\d+"))
            {
                docs = docs.Take(Convert.ToInt32(s.Scan(@"limit\s+(\d+)\s*", 1)));
            }

            // skip can be before or after limit command
            if (s.Match(@"skip\s+\d+"))
            {
                docs = docs.Skip(Convert.ToInt32(s.Scan(@"skip\s+(\d+)\s*", 1)));
            }

            return docs;
        }

        public Query ReadQuery(StringScanner s)
        {
            if (s.HasTerminated || s.Match(@"skip\s+\d") || s.Match(@"limit\s+\d"))
            {
                return Query.All();
            }
            else if(s.Scan(@"\(").Length > 0)
            {
                return this.ReadInlineQuery(s);
            }
            else
            {
                return this.ReadOneQuery(s);
            }
        }

        private Query ReadInlineQuery(StringScanner s)
        {
            var left = this.ReadOneQuery(s);

            if (s.Scan(@"\s*\)\s*").Length > 0)
            {
                return left;
            }

            var oper = s.Scan(@"\s*(and|or)\s*").Trim();

            if(oper.Length == 0) throw new ApplicationException("Invalid query operator");

            return oper == "and" ?
                Query.And(left, this.ReadInlineQuery(s)) :
                Query.Or(left, this.ReadInlineQuery(s));
        }

        private Query ReadOneQuery(StringScanner s)
        {
            var field = s.Scan(@"\w+(\.\w+)*\s*").Trim();
            var oper = s.Scan(@"(=|!=|>=|<=|>|<|like|in|between)");
            var value = new JsonReader().ReadValue(s);

            switch (oper)
            {
                case "=": return Query.EQ(field, value.RawValue);
                case "!=": return Query.Not(field, value.RawValue);
                case ">": return Query.GT(field, value.RawValue);
                case ">=": return Query.GTE(field, value.RawValue);
                case "<": return Query.LT(field, value.RawValue);
                case "<=": return Query.LTE(field, value.RawValue);
                case "like": return Query.StartsWith(field, value.AsString);
                case "in": return Query.In(field, value.AsArray.RawValue.ToArray());
                case "between": return Query.Between(field, value.AsArray.RawValue[0], value.AsArray.RawValue[1]);
                default: throw new ApplicationException("Invalid query operator");
            }
        }

    }
}
