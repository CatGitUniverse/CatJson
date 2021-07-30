﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;

namespace CatJson
{
    public static partial class JsonParser
    {

        /// <summary>
        /// 将指定类型的对象转换为Json文本
        /// </summary>
        public static string ToJson<T>(T obj, bool reflection = true) where T : new()
        {
            return ToJson(obj, typeof(T),reflection);
        }

        /// <summary>
        /// 将指定类型的对象转换为Json文本
        /// </summary>
        public static string ToJson(object obj, Type type, bool reflection = true)
        {
            if (reflection)
            {
                //反射转换
                AppendJsonObject(obj, type, 1);
            }
            else if (GenCodes.ToJsonCodeFuncDict.TryGetValue(type, out Action<object,int> action))
            {
                //使用预生成代码转换
                action(obj, 1);
            }


            if (Util.CachedSB.Length == 0)
            {
                throw new Exception($"没有为{type}类型预生成的转换代码");
            }

            string json = Util.CachedSB.ToString();
            Util.CachedSB.Clear();
            return json;
        }

        /// <summary>
        /// 追加Json数据类对象文本
        /// </summary>
        private static void AppendJsonObject(object obj, Type type, int depth)
        {
            Util.AppendLine("{");


            if (!propertyInfoDict.ContainsKey(type) && !fieldInfoDict.ContainsKey(type))
            {
                //初始化反射信息
                AddToReflectionMap(type);
            }

            bool flag = false;

            propertyInfoDict.TryGetValue(type, out Dictionary<RangeString, PropertyInfo> piDict);
            if (piDict != null)
            {
                foreach (KeyValuePair<RangeString, PropertyInfo> item in piDict)
                {
                    object value = item.Value.GetValue(obj);
                    Type piType = item.Value.PropertyType;
                    string piName = item.Value.Name;

                    if (Util.IsDefaultValue(piType,value))
                    {
                        //默认值跳过序列化
                        continue;
                    }
                    flag = true;

                   

                    AppendJsonKeyValue(piType, piName, value, depth);

                    Util.AppendLine(",");
                }
            }

            fieldInfoDict.TryGetValue(type, out Dictionary<RangeString, FieldInfo> fiDict);
            if (fiDict != null)
            {
                foreach (KeyValuePair<RangeString, FieldInfo> item in fiDict)
                {
                    object value = item.Value.GetValue(obj);
                    Type fiType = item.Value.FieldType;
                    string fiName = item.Value.Name;

                    if (Util.IsDefaultValue(fiType, value))
                    {
                        //默认值跳过序列化
                        continue;
                    }
                    flag = true;


                    AppendJsonKeyValue(fiType, fiName, value, depth);

                    Util.AppendLine(",");
                }

            }

            if (flag == true)
            {
                //删掉最后的 , 字符
                Util.CachedSB.Remove(Util.CachedSB.Length - 3, 1);
            }



            Util.Append("}", depth - 1);

        }

        /// <summary>
        /// 追加Json 键值对文本
        /// </summary>
        private static void AppendJsonKeyValue(Type valueType, string name, object value, int depth)
        {
            AppendJsonKey(name, depth);
            AppendJsonValue(valueType, value, depth);
        }

        /// <summary>
        /// 追加Json 值文本
        /// </summary>
        private static void AppendJsonValue(Type valueType, object value, int depth)
        {
            if (value == null)
            {
                Util.Append("null");
                return;
            }

            if (extensionToJsonFuncDict.TryGetValue(valueType, out Action<object> action))
            {
                //自定义转换Json文本方法
                action(value);
                return;
            }

            //根据属性值的不同类型进行序列化
            if (Util.IsNumber(valueType))
            {
                //数字
                Util.Append(value.ToString());
                return;
            }

            if (valueType == typeof(string))
            {
                //字符串
                Util.Append("\"");
                Util.Append(value.ToString());
                Util.Append("\"");
                return;
            }

            if (valueType == typeof(bool))
            {
                //bool值
                bool b = (bool)value;
                if (b == true)
                {
                    Util.Append("true");
                }
                else
                {
                    Util.Append("false");
                }
                return;
            }

            if (valueType.IsEnum)
            {
                //枚举 todo:
            }

            if (Util.IsArrayOrList(valueType))
            {
                //数组 或 List<T>
                Util.AppendLine("[");

                bool flag = false;

                if (valueType.IsArray)
                {
                    Array array = (Array)value;
                    for (int i = 0; i < array.Length; i++)
                    {
                        object element = array.GetValue(i);
                        if (element == null)
                        {
                            continue;
                        }

                        flag = true;

                       
                        Util.AppendTab(depth + 1);
                        AppendJsonValue(element.GetType(), element, depth);
                        Util.AppendLine(",");
                    }
                }
                else
                {
                    IList list = (IList)value;
                    for (int i = 0; i < list.Count; i++)
                    {
                        object element = list[i];
                        if (element == null)
                        {
                            continue;
                        }

                        flag = true;
                        Util.AppendTab(depth + 1);
                        AppendJsonValue(element.GetType(), element, depth);
                        Util.AppendLine(",");
                    }
                }

                if (flag)
                {
                    //删掉最后的 , 字符
                    Util.CachedSB.Remove(Util.CachedSB.Length - 3, 1);
                  
                }
                Util.Append("]", depth);


                return;
            }


            if (Util.IsDictionary(valueType))
            {
                //字典
                IDictionary dict = (IDictionary)value;
                IDictionaryEnumerator enumerator = dict.GetEnumerator();

                Util.AppendLine("{");

                bool flag = false;

                while (enumerator.MoveNext())
                {
                    //null值跳过
                    if (enumerator.Value == null)
                    {
                        continue;
                    }

                    flag = true;
                    AppendJsonKeyValue(enumerator.Value.GetType(), enumerator.Key.ToString(), enumerator.Value, depth + 1);
                    Util.AppendLine(",");
                }

                if (flag == true)
                {
                    //删掉最后的 , 字符
                    Util.CachedSB.Remove(Util.CachedSB.Length - 3, 1);
                }


                Util.Append("}", depth);

                return;
            }

            //自定义类对象
            AppendJsonObject(value, valueType, depth + 1);
        }



        public static void AppendJsonKey(string key, int depth)
        {
            Util.Append("\"", depth);
            Util.Append(key);
            Util.Append("\"");
            Util.Append(":");
        }

        public static void AppendJsonValue(bool b,int depth = 0)
        {
            if (b == true)
            {
                Util.Append("true",depth);
            }
            else
            {
                Util.Append("false",depth);
            }
            Util.AppendLine(",");
        }

        public static void AppendJsonValue(string s, int depth = 0)
        {
            Util.Append("\"",depth);
            Util.Append(s.ToString());
            Util.Append("\"");
            Util.AppendLine(",");
        }

        public static void AppendJsonValue(int i, int depth = 0)
        {
            Util.Append(i.ToString(),depth);
            Util.AppendLine(",");
        }

        public static void AppendJsonValue(long l, int depth = 0)
        {
            Util.Append(l.ToString(), depth);
            Util.AppendLine(",");
        }

        public static void AppendJsonValue(float f, int depth = 0)
        {
            Util.Append(f.ToString(), depth);
            Util.AppendLine(",");
        }

        public static void AppendJsonValue(double d, int depth = 0)
        {
            Util.Append(d.ToString(), depth);
            Util.AppendLine(",");
        }


    }

}
