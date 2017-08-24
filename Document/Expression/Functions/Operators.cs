﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text;

namespace LiteDB
{
    internal partial class LiteExpression
    {
        public static IEnumerable<BsonValue> EQ(IEnumerable<BsonValue> left, IEnumerable<BsonValue> right)
        {
            foreach (var value in Zip(left, right))
            {
                yield return value.Left == value.Right;
            }
        }

        public static IEnumerable<BsonValue> FILTER(IEnumerable<BsonValue> values, IEnumerable<BsonValue> conditional)
        {
            foreach (var value in Zip(values, conditional))
            {
                if(value.Right.IsBoolean && value.Right.AsBoolean)
                {
                    yield return value.Left;
                }
            }
        }
    }
}
