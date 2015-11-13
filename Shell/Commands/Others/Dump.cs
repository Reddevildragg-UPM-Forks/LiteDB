﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;

namespace LiteDB.Shell.Commands
{
    internal class Dump : ILiteCommand
    {
        public bool IsCommand(StringScanner s)
        {
            return s.Scan(@"dump\s*").Length > 0;
        }

        public BsonValue Execute(LiteDatabase db, StringScanner s)
        {
            if (s.HasTerminated)
            {
                return db.DumpPages();
            }
            else
            {
                var col = s.Scan(@"[\w-]+");
                var field = s.Scan(@"\s+\w+").Trim();

                return db.DumpIndex(col, field);
            }
        }
    }
}
