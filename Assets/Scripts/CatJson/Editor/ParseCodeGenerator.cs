﻿using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using System;
using System.Reflection;
using System.IO;
using System.Text;
namespace CatJson.Editor
{
    /// <summary>
    /// 解析代码生成器
    /// </summary>
    public static class ParseCodeGenerator
    {

        private static StringBuilder sb = new StringBuilder();

        /// <summary>
        /// 存放已生成过解析代码的Type
        /// </summary>
        private static HashSet<Type> GenParseCodeTypes = new HashSet<Type>();

        /// <summary>
        /// 存放待生成解析代码的被Root依赖的Type
        /// </summary>
        private static Queue<Type> needGenTypes = new Queue<Type>();



        [MenuItem("CatJson/预生成Json解析代码")]
        private static void GenParseCode()
        {

            if (!Directory.Exists(ParseCodeGenConfig.GenCodeDirPath))
            {
                Directory.CreateDirectory(ParseCodeGenConfig.GenCodeDirPath);
            }
            else
            {
                //清空旧文件
                DirectoryInfo di = new DirectoryInfo(ParseCodeGenConfig.GenCodeDirPath);
                foreach (FileInfo fi in di.GetFiles())
                {
                    fi.Delete();
                }
            }

            GenParseCodeTypes.Clear();
            needGenTypes.Clear();

            //生成Root的解析代码
            List<Type> types = GetGenParseCodeTypes();
            for (int i = 0; i < types.Count; i++)
            {
                Type type = types[i];
                GenParseCode(type);
                GenParseCodeTypes.Add(type);
            }

            //生成被Root依赖的Type的解析代码
            while (needGenTypes.Count > 0)
            {
                Type type = needGenTypes.Dequeue();
                GenParseCode(type);
            }

            //生成静态构造器文件
            GenStaticCtorCode(types);

            AssetDatabase.Refresh();
        }


        /// <summary>
        /// 生成解析代码文件
        /// </summary>
        private static void GenParseCode(Type type)
        {
            //读取模板文件
            StreamReader sr;

            if (type.IsValueType)
            {
                //值类型
                sr = new StreamReader(ParseCodeGenConfig.ParseStructCodeTemplateFilePath);
            }
            else
            {
                //引用类型
                sr = new StreamReader(ParseCodeGenConfig.ParseClassCodeTemplateFilePath);
            }

            string template = sr.ReadToEnd();
            sr.Close();

            //写入using
            template = template.Replace("#Using#", AppendUsingCode());

            //写入类名
            template = template.Replace("#ClassName#", type.FullName);

            //写入解析方法名
            template = template.Replace("#MethodName#", GetParseMethodName(type));

            //生成解析代码
            template = template.Replace("#IfElseParse#", AppendIfElseParseCode(type));

            StreamWriter sw = new StreamWriter($"{ParseCodeGenConfig.GenCodeDirPath}/Gen_{type.FullName.Replace(".", "_")}_ParseCode.cs");
            sw.Write(template);
            sw.Close();
        }

        /// <summary>
        /// 生成静态构造器文件
        /// </summary>
        private static void GenStaticCtorCode(List<Type> types)
        {
            //读取模板文件
            StreamReader sr = new StreamReader(ParseCodeGenConfig.StaticCtorTemplateFilePath);
            string template = sr.ReadToEnd();
            sr.Close();

            foreach (Type type in types)
            {
                AppendLine($"ParseCodeFuncDict.Add(typeof({type.FullName}),{GetParseMethodName(type)});", 3);
            }

            template = template.Replace("#AddParseCodeFunc#", sb.ToString());
            sb.Clear();

            StreamWriter sw = new StreamWriter($"{ParseCodeGenConfig.GenCodeDirPath}/Gen_ParseCodeStaticCtor.cs");
            sw.Write(template);
            sw.Close();
        }


        /// <summary>
        /// 生成using代码
        /// </summary>
        private static string AppendUsingCode()
        {
            AppendLine("using System;",0);
            AppendLine("using System.Collections.Generic;",0);
            string result = sb.ToString();
            sb.Clear();
            return result;
        }

        /// <summary>
        /// 生成使用ifelse进行所有字段/属性解析的代码
        /// </summary>
        private static string AppendIfElseParseCode(Type type)
        {
            //处理属性
            bool isElseIf = false;
            foreach (PropertyInfo pi in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                //属性必须同时具有get set，并且不能是Item
                if (pi.SetMethod != null && pi.GetMethod != null && pi.Name != "Item")
                {
                    AppendIfElseCode(pi.PropertyType, pi.Name,isElseIf);

                    if (!isElseIf)
                    {
                        isElseIf = true;
                    }
                   
                }
               
            }

            //处理字段
            isElseIf = false;
            foreach (FieldInfo fi in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                AppendIfElseCode(fi.FieldType, fi.Name,isElseIf);

                if (!isElseIf)
                {
                    isElseIf = true;
                }
            }

            string result = sb.ToString();
            sb.Clear();

            return result;
        }

        /// <summary>
        /// 生成解析单个字段/属性的ifelse代码
        /// </summary>
        private static void AppendIfElseCode(Type type,string name,bool isElseIf)
        {
            if (isElseIf)
            {
                AppendLine($"else if (key.Equals(new RangeString(\"{name}\")))");
            }
            else
            {
                AppendLine($"if (key.Equals(new RangeString(\"{name}\")))");
            }

            AppendLine("{");

            //基础类型
            if (type == typeof(string))
            {
                AppendAssignmentCode($"temp.{name} = rs.Value.ToString();");
            }
            else if (type == typeof(bool))
            {
                AppendAssignmentCode($"temp.{name} = tokenType == TokenType.True;");
            }
            else if (Util.IsNumber(type))
            {
                AppendAssignmentCode($"temp.{name} = {type.FullName}.Parse(rs.Value.ToString());");
            }
            else if (Util.IsArrayOrList(type))
            {
                //数组和List<T>
                Type elementType;
                if (type.IsArray)
                {
                    elementType = type.GetElementType();
                }
                else
                {
                    elementType = type.GetGenericArguments()[0];
                }

                AppendLine($"List<{elementType.FullName}> list = new List<{elementType.FullName}>();");

                AppendParseArrayCode(name, elementType);

                if (type.IsArray)
                {
                    AppendLine($"temp.{name} = list.ToArray();");
                }
                else
                {
                    AppendLine($"temp.{name} = list;");
                }
            }
            else if (Util.IsDictionary(type))
            {
                //字典
                Type valueType = type.GetGenericArguments()[1];
                string valueTypeName = GetDictValueTypeName(type);
                AppendLine($"Dictionary<string, {valueTypeName}> dict = new Dictionary<string, {valueTypeName}>();");
                AppendParseDictCode("dict", "userdata11", "userdata21", "key1", "nextTokenType1", valueType);
                AppendLine($"temp.{name} = dict;");
            }
            else
            {
                //其他类型
                AppendLine($"temp.{name} = {GetParseMethodName(type)}();");

                if (!GenParseCodeTypes.Contains(type))
                {
                    needGenTypes.Enqueue(type);
                }
            }

            AppendLine("}");

        }

        /// <summary>
        /// 生成赋值代码
        /// </summary>
        private static void AppendAssignmentCode(string insertCode)
        {
            AppendLine("rs = JsonParser.Lexer.GetNextToken(out tokenType);");
            AppendLine(insertCode);
        }

        /// <summary>
        /// 生成解析json数组的代码
        /// </summary>
        private static void AppendParseArrayCode(string name,Type elementType)
        {
            AppendLine($"JsonParser.ParseJsonArrayProcedure(list, null, (userdata11, userdata21, nextTokenType1) =>");
            AppendLine("{");

            //基础类型
            if (elementType == typeof(string))
            {
                AppendAssignmentCode( $"((List<{elementType.FullName}>)userdata11).Add(rs.Value.ToString());");
            }
            else if (elementType == typeof(bool))
            {
                AppendAssignmentCode($"((List<{elementType.FullName}>)userdata11).Add(tokenType == TokenType.True);");
            }
            else if (Util.IsNumber(elementType))
            {
                AppendAssignmentCode( $"((List<{elementType.FullName}>)userdata11).Add({elementType.FullName}.Parse(rs.Value.ToString()));");
            }
            else if (Util.IsArrayOrList(elementType))
            {
                //数组 List<T> todo:
            }
            else if (Util.IsDictionary(elementType))
            {
                //字典 todo:
            }
            else
            {
                //自定义类型
                AppendLine($"((List<{elementType.FullName}>)userdata11).Add({GetParseMethodName(elementType)}());");

                if (!GenParseCodeTypes.Contains(elementType))
                {
                    needGenTypes.Enqueue(elementType);
                }
            }
         

            AppendLine("});");
        }

        /// <summary>
        /// 生成解析字典的代码
        /// </summary>
        private static void AppendParseDictCode(string dictName,string userdata1Name,string userdata2Name,string keyName,string nextTokenTypeName,Type valueType)
        {
            AppendLine($"JsonParser.ParseJsonObjectProcedure({dictName}, null, ({userdata1Name}, {userdata2Name},{keyName}, {nextTokenTypeName}) =>");
            AppendLine("{");

            //基础类型
            if (valueType == typeof(string))
            {
                AppendLine($"((Dictionary<string, {valueType.FullName}>){userdata1Name}).Add({keyName}.ToString(),JsonParser.Lexer.GetNextToken(out _).Value.ToString());");
            }
            else if (valueType == typeof(bool))
            {
                AppendLine("JsonParser.Lexer.GetNextToken(out tokenType);");
                AppendLine($"((Dictionary<string, {valueType.FullName}>){userdata1Name}).Add({keyName}.ToString(),tokenType == TokenType.True);");
            }
            else if (Util.IsNumber(valueType))
            {
                AppendLine($"((Dictionary<string, {valueType.FullName}>){userdata1Name}).Add({keyName}.ToString(), {valueType.FullName}.Parse(JsonParser.Lexer.GetNextToken(out _).Value.ToString()));");
            }
            else if (Util.IsArrayOrList(valueType))
            {
                //数组 List<T> todo:
            }
            else if (Util.IsDictionary(valueType))
            {
                //字典
                Type newValueType = valueType.GetGenericArguments()[1];
                string newValueTypeName = GetDictValueTypeName(valueType);
                AppendLine($"Dictionary<string, {newValueTypeName}> {dictName}1 = new Dictionary<string, {newValueTypeName}>();");
                AppendParseDictCode($"{dictName}1", $"{userdata1Name}1", $"{userdata2Name}1", $"{keyName}1", $"{nextTokenTypeName}1", newValueType);
                AppendLine($"((Dictionary<string, Dictionary<string,{newValueTypeName}>>){userdata1Name}).Add({keyName}.ToString(),{dictName}1);");
            }
            else
            {
                //自定义类型
                AppendLine($"((Dictionary<string, {valueType.FullName}>){userdata1Name}).Add({keyName}.ToString(),{GetParseMethodName(valueType)}());");

                if (!GenParseCodeTypes.Contains(valueType))
                {
                    needGenTypes.Enqueue(valueType);
                }
            }

            AppendLine("});");
        }

        /// <summary>
        /// 获取需要生成解析代码的json数据类
        /// </summary>
        private static List<Type> GetGenParseCodeTypes()
        {
            List<Type> types = new List<Type>();
            foreach (string item in ParseCodeGenConfig.Assemblies)
            {
                Assembly assembly = Assembly.Load(item);
                foreach (Type type in assembly.GetTypes())
                {
                    if (type.GetCustomAttribute<GenParseCodeRootAttribute>() != null)
                    {
                        types.Add(type);
                    }
                }
            }

            return types;
        }

     

        /// <summary>
        /// 带制表符的AppendLine
        /// </summary>
        private static void AppendLine(string str, int tabNum = 4)
        {
            for (int i = 0; i < tabNum; i++)
            {
                sb.Append("\t");
            }
            sb.AppendLine(str);
        }

        /// <summary>
        /// 获取类型对应的解析方法名
        /// </summary>
        private static string GetParseMethodName(Type type)
        {
            return $"Parse_{type.FullName.Replace(".", "_")}";
        }

        /// <summary>
        /// 获取字典的正确value类型名
        /// </summary>
        private static string GetDictValueTypeName(Type dictType)
        {
            Type valueType = dictType.GetGenericArguments()[1];
            if (!Util.IsDictionary(valueType))
            {
                return valueType.FullName;
            }

            return $"Dictionary<string,{GetDictValueTypeName(valueType)}>";
        }
    }
}

