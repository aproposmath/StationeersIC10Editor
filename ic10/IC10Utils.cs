namespace StationeersIC10Editor.IC10;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;

using Assets.Scripts;
using Assets.Scripts.Atmospherics;
using Assets.Scripts.Objects;
using Assets.Scripts.Objects.Electrical;
using Assets.Scripts.Objects.Entities;
using Assets.Scripts.Objects.Motherboards;
using Assets.Scripts.Objects.Pipes;

using Objects.Rockets;

public enum DataType : uint
{
    Unknown = 0x00,
    Register = 0x01,
    Device = 0x02,
    LogicType = 0x04,
    Number = 0x08,
    Label = 0x10,
    Instruction = 0x20,
    BatchMode = 0x40,
    Comment = 0x80,
    Identifier = 0x100,
    Alias = 0x200,
    Define = 0x400,
    LogicSlotType = 0x800,
    ReagentMode = 0x1000,
    Color = 0x2000,
    DeviceId = 0x4000,
    BasicEnum = 0x8000,
}

public class IC10Utils
{
    private static HashSet<string> _registers = new HashSet<string>();
    private static HashSet<string> _logicTypes = new HashSet<string>();
    private static HashSet<string> _logicSlotTypes = new HashSet<string>();
    private static HashSet<string> _devices = new HashSet<string>();
    private static HashSet<string> _constants = new HashSet<string>();
    private static HashSet<string> _batchModes = new HashSet<string>();
    private static HashSet<string> _basicEnums = new HashSet<string>();
    private static Dictionary<string, IC10OpCode> _instructions =
        new Dictionary<string, IC10OpCode>();
    private static Dictionary<string, ArgType> _types = new Dictionary<string, ArgType>();

    public static Dictionary<string, uint> Colors = new Dictionary<string, uint>
    {
        { "Color.Blue", ICodeFormatter.ColorFromHTML("blue") },
        { "Color.Gray", ICodeFormatter.ColorFromHTML("gray") },
        { "Color.Green", ICodeFormatter.ColorFromHTML("green") },
        { "Color.Orange", ICodeFormatter.ColorFromHTML("orange") },
        { "Color.Red", ICodeFormatter.ColorFromHTML("red") },
        { "Color.Yellow", ICodeFormatter.ColorFromHTML("yellow") },
        { "Color.White", ICodeFormatter.ColorFromHTML("white") },
        { "Color.Black", ICodeFormatter.ColorFromHTML("black") },
        { "Color.Brown", ICodeFormatter.ColorFromHTML("brown") },
        { "Color.Khaki", ICodeFormatter.ColorFromHTML("khaki") },
        { "Color.Pink", ICodeFormatter.ColorFromHTML("pink") },
        { "Color.Purple", ICodeFormatter.ColorFromHTML("purple") },
    };

    public static bool IsBuiltin(string name)
    {
        return Types.ContainsKey(name);
    }

    public static ArgType GetType(string name)
    {
        if (Types.TryGetValue(name, out ArgType type))
            return type;
        return DataType.Unknown;
    }

    private static void _addType(string name, DataType type)
    {
        if (!_types.ContainsKey(name))
            _types[name] = type;
        else
        {
            var t = _types[name];
            t.Add(type);
            _types[name] = t;
        }
    }

    static void _addEnum<T>(string name = "")
    {
        if (!string.IsNullOrEmpty(name))
            name = name + ".";

        foreach (var enumName in Enum.GetNames(typeof(T)))
        {
            _addType(name + enumName, DataType.BasicEnum);
            _basicEnums.Add(name + enumName);
        }
    }

    public static Dictionary<string, ArgType> Types
    {
        get
        {
            if (_types.Count == 0)
            {
                _addEnum<SoundAlert>("Sound");
                _addEnum<LogicTransmitterMode>("TransmitterMode");
                _addEnum<ElevatorMode>("ElevatorMode");
                _addEnum<ColorType>("Color");
                _addEnum<EntityState>("EntityState");
                _addEnum<AirControlMode>("AirControl");
                _addEnum<DaylightSensor.DaylightSensorMode>("DaylightSensorMode");
                _addEnum<ConditionOperation>();
                _addEnum<AirConditioningMode>("AirCon");
                _addEnum<VentDirection>("Vent");
                _addEnum<PowerMode>("PowerMode");
                _addEnum<RobotMode>("RobotMode");
                _addEnum<SortingClass>("SortingClass");
                _addEnum<Slot.Class>("SlotClass");
                _addEnum<Chemistry.GasType>("GasType");
                _addEnum<RocketMode>("RocketMode");
                _addEnum<ReEntryProfile>("ReEntryProfile");
                _addEnum<SorterInstruction>("SorterInstruction");
                _addEnum<PrinterInstruction>("PrinterInstruction");
                _addEnum<TraderInstruction>("TraderInstruction");
                _addEnum<ShuttleType>("ShuttleType");
                _addEnum<HashType>("HashType");
                _addEnum<LogicDisplay.DisplayMode>("DisplayMode");
                _addEnum<SettingDisplayMode>("SettingDisplayMode");
                _addEnum<LogicType>("LogicType");
                _addEnum<LogicSlotType>("LogicSlotType");

                foreach (var reg in Registers)
                    _addType(reg, DataType.Register);
                foreach (var dev in Devices)
                    _addType(dev, DataType.Device);
                foreach (var lt in LogicTypes)
                    _addType(lt, DataType.LogicType);
                foreach (var lt in LogicSlotTypes)
                    _addType(lt, DataType.LogicSlotType);
                foreach (var constant in Constants)
                    _addType(constant, DataType.Number);
                foreach (var bm in BatchModes)
                    _addType(bm, DataType.BatchMode);
                foreach (var instr in Instructions.Keys)
                    _addType(instr, DataType.Instruction);
                foreach (var color in Colors.Keys)
                    _addType(color, DataType.Color);

                _types["define"] = DataType.Define;
                _types["alias"] = DataType.Alias;

                foreach (var prefab in Prefab.AllPrefabs)
                {
                    if (prefab as ILogicable == null)
                        continue;

                    _addType("HASH(\"" + prefab.PrefabName + "\")", DataType.Number);
                }
            }
            return _types;
        }
    }

    public static HashSet<string> BatchModes
    {
        get
        {
            if (_batchModes.Count == 0)
                foreach (LogicType lt in EnumCollections.LogicBatchMethods.Values)
                    _batchModes.Add(Enum.GetName(typeof(LogicBatchMethod), lt));
            return _batchModes;
        }
    }

    public static string GetLogicablePrefabName(string hashExpression)
    {
        if (IsHashExpression(hashExpression))
        {
            var dt = GetType(hashExpression);
            if (!dt.Has(DataType.Number))
                return null;
            return hashExpression.Substring(6, hashExpression.Length - 8);
        }

        if (TryParseNumber(hashExpression, out double hashNum))
        {
            int hashValue = (int)hashNum;
            var thing = Prefab.Find<Thing>(hashValue);
            if (thing != null && thing is ILogicable)
                return thing.PrefabName;
            return null;
        }

        return null;
    }

    public static bool TryParseNumber(string text, out double value)
    {
        value = double.NaN;
        if (text.StartsWith("$"))
        {
            if (Int64.TryParse(text.Substring(1), System.Globalization.NumberStyles.HexNumber, null, out long hexValue))
            {
                value = (double)hexValue;
                return true;
            }
            return false;
        }
        if (text.StartsWith("%"))
        {
            string digits = text.Substring(1).Replace("_", "");
            try
            {
                value = (double)Convert.ToInt64(digits, 2);
                return true;
            }
            catch
            {
                return false;
            }
        }
        if (Double.TryParse(text, out value))
            return true;
        return false;
    }

    public static HashSet<string> Registers
    {
        get
        {
            if (_registers.Count == 0)
            {
                for (int i = 0; i < 18; i++)
                {
                    _registers.Add($"r{i}");
                    _registers.Add($"rr{i}");
                    _registers.Add($"rrr{i}");
                    _registers.Add($"rrrr{i}");
                    if (i < 10)
                    {
                        _registers.Add($"r0{i}");
                        _registers.Add($"rr0{i}");
                        _registers.Add($"rrr0{i}");
                        _registers.Add($"rrrr0{i}");
                    }
                }
                _registers.Add("sp");
                _registers.Add("ra");
            }
            return _registers;
        }
    }

    public static HashSet<string> Devices
    {
        get
        {
            if (_devices.Count == 0)
            {
                for (int i = 0; i < 6; i++)
                    _devices.Add($"d{i}");
                for (int i = 0; i < 18; i++)
                {
                    _devices.Add($"dr{i}");
                    _devices.Add($"drr{i}");
                    _devices.Add($"drrr{i}");
                    _devices.Add($"drrrr{i}");
                    if (i < 10)
                    {
                        _registers.Add($"dr0{i}");
                        _registers.Add($"drr0{i}");
                        _registers.Add($"drrr0{i}");
                        _registers.Add($"drrrr0{i}");
                    }
                }
                _devices.Add("db");
            }
            return _devices;
        }
    }

    public static HashSet<string> BasicEnums => _basicEnums;

    public static HashSet<string> LogicTypes
    {
        get
        {
            if (_logicTypes.Count == 0)
                foreach (LogicType lt in EnumCollections.LogicTypes.Values)
                    _logicTypes.Add(Enum.GetName(typeof(LogicType), lt));
            return _logicTypes;
        }
    }

    public static HashSet<string> LogicSlotTypes
    {
        get
        {
            if (_logicSlotTypes.Count == 0)
                foreach (LogicSlotType lt in EnumCollections.LogicSlotTypes.Values)
                    _logicSlotTypes.Add(Enum.GetName(typeof(LogicSlotType), lt));
            return _logicSlotTypes;
        }
    }

    public static HashSet<string> Constants
    {
        get
        {
            if (_constants.Count == 0)
                foreach (var constant in ProgrammableChip.AllConstants)
                    _constants.Add(constant.Literal);
            return _constants;
        }
    }

    public static Dictionary<string, IC10OpCode> Instructions
    {
        get
        {
            if (_instructions.Count == 0)
                _initInstructions();
            return _instructions;
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
    private static void _initInstructions()
    {
        foreach (ScriptCommand cmd in EnumCollections.ScriptCommands.Values)
        {
            string cmdName = Enum.GetName(typeof(ScriptCommand), cmd);
            var description = ProgrammableChip.GetCommandDescription(cmd);
            var example = ProgrammableChip.GetCommandExample(cmd);
            _instructions[cmdName] = new IC10OpCode(cmdName, description, example);
        }
        Assembly[] assemblies = System.AppDomain.CurrentDomain.GetAssemblies();
        foreach (var assembly in assemblies)
        {
            string message = assembly.GetName().Name;
            if (message.Equals("IC10Extender"))
            {
                var opcodesObj = assembly
                    .GetType("IC10_Extender.IC10Extender")
                    ?.GetProperty("OpCodes", BindingFlags.Public | BindingFlags.Static)
                    ?.GetValue(null);

                if (opcodesObj == null)
                {
                    L.Warning("IC10Extender: could not get OpCodes property");
                    continue;
                }

                var keysProp = opcodesObj.GetType().GetProperty("Keys");
                if (keysProp?.GetValue(opcodesObj) is not IEnumerable<string> opcodes)
                    continue;

                var indexer = opcodesObj.GetType().GetProperty("Item");

                foreach (var name in opcodes)
                {
                    var opcode = indexer.GetValue(opcodesObj, new[] { name });
                    L.Debug(
                        $"IC10Extender: Found opcode {name}, type: {opcode.GetType().FullName}"
                    );
                    var example =
                        name
                            + " "
                            + opcode
                                .GetType()
                                .GetMethod("CommandExample", [typeof(int), typeof(string)])
                                ?.Invoke(opcode, [0, null])
                        as string;
                    L.Debug($"  Example: {example}");
                    var description =
                        opcode.GetType().GetMethod("Description")?.Invoke(opcode, null) as string;
                    L.Debug($"  Description: {description}");
                    Instructions[name] = new IC10OpCode(name, description, example);
                }
                break;
            }
        }
    }

    public static string Minify(List<StyledLine> lines)
    {
        List<IC10Line> minified = new List<IC10Line>();
        uint iOld = 0;
        uint iNew = 0;

        Dictionary<uint, uint> lineMap = new Dictionary<uint, uint>();
        Dictionary<string, string> defines = new Dictionary<string, string>();

        foreach (var sline in lines)
        {
            var line = sline as IC10Line;
            lineMap[iOld] = iNew;

            if (line.Text.EndsWith("# KEEP"))
            {
                minified.Add(line);
                iNew++;
            }
            else if (line.IsLabel)
            {
                string labelName = line[0].Text.TrimEnd(':');
                defines[labelName] = iNew.ToString();
            }
            else if (line.IsDefine)
            {
                defines[line[1].Text] = line[2].Text;
            }
            else if (line.IsAlias)
            {
                if (defines.ContainsKey(line[1].Text))
                    defines[line[1].Text] = null;
                else
                    defines[line[1].Text] = line[2].Text;
                // skip alias in second pass (and only if it's not overwritten in the code)
                minified.Add(line);

            }
            else if (line.NumCodeTokens > 0)
            {
                minified.Add(line);
                iNew++;
            }
            iOld++;
        }

        var newCode = new StringBuilder();
        foreach (var line in minified)
        {
            if (line.IsAlias && defines.TryGetValue(line[1].Text, out string aliasValue) && aliasValue != null)
                continue;

            string lineStr = "";
            foreach (var t in line)
            {
                string text = t.Text;
                string suffix = "";
                if (text.Contains(":"))
                {
                    suffix = text.Substring(text.IndexOf(':'));
                    text = text.Substring(0, text.IndexOf(':'));
                }
                if (defines.TryGetValue(text, out string value) && value != null)
                    text = value;
                if (!text.TrimStart().StartsWith("#"))
                    lineStr += text.Trim() + suffix + " ";
            }
            newCode.AppendLine(lineStr.Trim());
        }
        return newCode.ToString().Trim();
    }

    public static bool IsHashExpression(string text) =>
        text.StartsWith("HASH(\"") && text.EndsWith("\")");

    public static bool IsStringExpression(string text) =>
        text.StartsWith("STR(\"") && text.EndsWith("\")");

    public static bool IsBinaryExpression(string text)
    {
        if (!text.StartsWith("%"))
            return false;
        string digits = text.Substring(1).Replace("_", "");
        foreach (char c in digits)
            if (c != '0' && c != '1')
                return false;
        return true;
    }

}

public struct ArgType
{
    uint Value = 0;

    public ArgType() { }

    public uint ToUInt()
    {
        return Value;
    }

    public static implicit operator ArgType(DataType b)
    {
        ArgType at = new ArgType();
        at.Value = (uint)b;
        return at;
    }

    public bool Has(DataType b)
    {
        return (Value & (uint)b) != 0;
    }

    public bool Has(ArgType b)
    {
        return (Value & b.Value) != 0;
    }

    public void Add(DataType b, DataType b2 = 0, DataType b3 = 0, DataType b4 = 0, DataType b5 = 0)
    {
        Value |= (uint)b;
        Value |= (uint)b2;
        Value |= (uint)b3;
        Value |= (uint)b4;
        Value |= (uint)b5;
    }

    public DataType ToDataType()
    {
        foreach (DataType dt in Enum.GetValues(typeof(DataType)))
        {
            if ((Value & (uint)dt) != 0)
                return dt;
        }
        return DataType.Unknown;
    }

    public string Description
    {
        get
        {
            List<DataType> types = new List<DataType>();

            foreach (DataType dt in Enum.GetValues(typeof(DataType)))
            {
                if ((Value & (uint)dt) != 0)
                    types.Add(dt);
            }

            if (types.Count == 0)
                return "Unknown";

            if (types.Count == 1)
                return types[0].ToString();

            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < types.Count; i++)
            {
                sb.Append(types[i].ToString());
                if (i < types.Count - 1)
                    sb.Append(" or ");
            }

            return sb.ToString();
        }
    }

    public ArgType Compat
    {
        get
        {
            ArgType at = new ArgType();
            at.Value = this.Value;
            if (Has(DataType.Number))
            {
                at.Add(
                    DataType.Label,
                    DataType.Register
                );
                at.Add(DataType.Color, DataType.BasicEnum);
            }
            if (Has(DataType.Label | DataType.DeviceId | DataType.BatchMode | DataType.LogicType | DataType.ReagentMode))
                at.Add(DataType.Number, DataType.Register);
            return at;
        }
    }

    public ArgType CommonType(ArgType other)
    {
        ArgType common = new ArgType();
        common.Value = this.Value & other.Value;
        return common;
    }
}

public class IC10OpCode
{
    public string Name = String.Empty;
    public string Description = String.Empty;
    public List<ArgType> ArgumentTypes = new List<ArgType>();
    public bool IsBuiltin = true;

    public StyledText Tooltip = new StyledText();

    public IC10OpCode(string name, string description, string example)
    {
        IsBuiltin = true;
        Name = name;
        Description = description;
        float w = 0.0f;
        float wmax = 0.0f;
        StyledText TooltipHeader = ParseColoredText($"Instruction: <color=yellow>{name}</color>", ref wmax);
        StyledLine TooltipExample = ParseColoredText($"    {example}", ref w)[0];
        wmax = Math.Max(wmax, w);

        int i = 1;
        while (i < TooltipExample.Count)
        {
            var s = TooltipExample[i].Text;
            if (s.Contains("(") || s.Contains(")"))
            {
                i++;
                continue;
            }

            while (i + 2 < TooltipExample.Count && TooltipExample[i + 1].Text.Trim() == "|")
            {
                s += TooltipExample[i + 1].Text;
                s += TooltipExample[i + 2].Text;
                i += 2;
            }

            i++;

            ArgType argType = new ArgType();

            foreach (var token in s.Split(new char[] { '|' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var trimmed = token.Trim().TrimEnd(',', ')');
                switch (trimmed)
                {
                    case "r?":
                        argType.Add(DataType.Register);
                        if (ArgumentTypes.Count > 0)
                            argType.Add(DataType.Number);
                        break;
                    case "d?":
                        argType.Add(DataType.Device);
                        break;
                    case "id":
                        argType.Add(DataType.DeviceId);
                        break;
                    case "num":
                    case "int":
                    case "deviceHash":
                    case "nameHash":
                    case "slotIndex":
                        argType.Add(DataType.Number, DataType.Register);
                        break;
                    case "logicType":
                        argType.Add(DataType.LogicType);
                        break;
                    case "logicSlotType":
                        argType.Add(DataType.LogicSlotType);
                        break;
                    case "batchMode":
                        argType.Add(DataType.BatchMode);
                        break;
                    case "reagentMode":
                        argType.Add(DataType.ReagentMode);
                        break;
                    case "str":
                        argType.Add(DataType.Identifier, DataType.Label, DataType.Device);
                        break;
                    default:
                        L.Warning($"Unknown argument token '{trimmed}' in opcode {name}");
                        break;
                }
            }

            ArgumentTypes.Add(argType);
        }

        Tooltip = new StyledText();
        Tooltip.AddRange(TooltipHeader);
        Tooltip.Add(new StyledLine(""));
        Tooltip.Add(TooltipExample);
        Tooltip.Add(new StyledLine(""));
        var descriptionLine = "";
        int column = 0;
        foreach (var word in Description.Split(' '))
        {
            if (column + word.Length > 70)
            {
                Tooltip.Add(new StyledLine(descriptionLine));
                descriptionLine = "";
                column = 0;
            }
            descriptionLine += (descriptionLine.Length > 0 ? " " : "") + word;
            column += word.Length + 1;
        }

        if (descriptionLine.Length > 0)
            Tooltip.Add(new StyledLine(descriptionLine));
    }

    public static StyledText ParseColoredText(string input, ref float width)
    {
        var result = new StyledText();
        var lines = input.Split('\n');
        var C = (Func<string, uint>)((string color) => ICodeFormatter.ColorFromHTML(color));

        foreach (var lineStr in lines)
        {
            var regex = new Regex(@"<color=(.*?)>(.*?)</color>", RegexOptions.Singleline);
            int lastIndex = 0;

            // We need to build the "Clean" text for the Line content
            StringBuilder cleanText = new StringBuilder();
            // We need to track where semantic tokens map to the clean text
            List<Token> tokens = new List<Token>();

            int currentColumn = 0;

            foreach (Match match in regex.Matches(lineStr))
            {
                // Plain text before match
                if (match.Index > lastIndex)
                {
                    string rawText = lineStr.Substring(lastIndex, match.Index - lastIndex);
                    cleanText.Append(rawText);
                    // Add a token for plain text with default color
                    if (!string.IsNullOrWhiteSpace(rawText))
                        tokens.Add(
                            new Token(currentColumn, rawText, C("#ffffff"))
                        );
                    currentColumn += rawText.Length;
                }

                // Colored text
                string colorStr = match.Groups[1].Value;
                string text = match.Groups[2].Value;
                cleanText.Append(text);
                if (!string.IsNullOrWhiteSpace(text))
                    tokens.Add(new Token(currentColumn, text, C(colorStr)));
                currentColumn += text.Length;

                lastIndex = match.Index + match.Length;
            }

            // Plain text after last match
            if (lastIndex < lineStr.Length)
            {
                string rawText = lineStr.Substring(lastIndex);
                cleanText.Append(rawText);
                if (!string.IsNullOrWhiteSpace(rawText))
                    tokens.Add(new Token(currentColumn, rawText, C("#ffffff")));
            }

            // Create the Line with clean text
            var resultLine = new StyledLine(cleanText.ToString());
            foreach (var t in tokens)
                resultLine.Add(t);

            result.Add(resultLine);
        }
        return result;
    }

}

// IC10Line updated to work with SemanticToken logic
public class IC10Line : StyledLine
{
    public IC10Line()
        : base()
    {
        // Tokens are populated by IC10CodeFormatter
    }

    public bool IsLabel => NumCodeTokens == 1 && GetDataType(0) == DataType.Label;
    public bool IsAlias => NumCodeTokens == 3 && GetDataType(0) == DataType.Alias;
    public bool IsNumAlias =>
        IsAlias && (GetDataType(2) == DataType.Number || GetDataType(2) == DataType.Register);
    public bool IsDevAlias => IsAlias && GetDataType(2) == DataType.Device;
    public bool IsDefine =>
        NumCodeTokens == 3 && GetDataType(0) == DataType.Define && GetDataType(2) == DataType.Number;
    public bool IsInstruction => NumCodeTokens > 0 && GetDataType(0) == DataType.Instruction;
    public bool IsJump => IsInstruction && (OpCode.StartsWith("j") || OpCode.StartsWith("b"));
    public bool IsRelativeJump => IsJump && (OpCode == "jr" || OpCode.StartsWith("br"));
    public bool IsBatchInstruction => IsInstruction && NumCodeTokens > 2 && (OpCode.StartsWith("lb") || OpCode.StartsWith("sb"));

    public string OpCode => IsInstruction ? this[0].Text : String.Empty;
    public int DeviceHashArgumentIndex => IsBatchInstruction ? (OpCode.StartsWith("lb") ? 1 : 0) : -1;

    // Counts actual semantic tokens (excluding whitespace/comments if they are treated as tokens,
    // but in SemanticToken model, everything interesting is a token.
    // We usually care about non-comment tokens for logic.)
    public int NumCodeTokens
    {
        get
        {
            if (Count == 0)
                return 0;
            var lastToken = this[Count - 1];
            return lastToken.Text.StartsWith("#") ? Count - 1 : Count;
        }
    }

    public DataType GetDataType(int index)
    {
        // This is tricky because Tokens list might contain comments or be sorted by column.
        // We need the i-th *code* token.
        int current = 0;
        foreach (var t in this)
        {
            if ((DataType)t.Type == DataType.Comment)
                continue;
            if (current == index)
                return (DataType)t.Type;
            current++;
        }
        return DataType.Unknown;
    }

    // This method logic was moved to IC10CodeFormatter.IdentifyTypesAndAddTokens
    // We keep a simplified version here if needed for updates, or remove it.
    // Since IC10CodeFormatter calls specific logic, we can rely on that.
    // But if we want to update types dynamically (e.g. after alias change):

    public void UpdateTokenColors(Dictionary<string, DataType> types, HashSet<string> changedNames = null)
    {
        // We need to re-evaluate token types.
        // Since SemanticToken is a struct, we iterate by index to modify.
        for (int i = 0; i < Count; i++)
        {
            var t = this[i];
            string text = t.Text;
            bool isUnknown = false;

            // Re-resolve type if it was an identifier
            if (types.TryGetValue(text, out DataType newType))
            {
                isUnknown = newType == DataType.Unknown;
                if (!isUnknown)
                {
                    t.Error = null;
                    t.Type = (uint)newType;
                    t.Style = new Style(IC10CodeFormatter.GetColor(newType, text),
                     IC10CodeFormatter.GetBackgroundColor(newType, text));
                }
            }
            else if (changedNames != null && changedNames.Contains(text))
                isUnknown = true;

            if (isUnknown)
            {
                t.Error = StyledText.ErrorText("Undefined identifier");
                t.Type = (uint)DataType.Unknown;
                t.Style = new Style(IC10CodeFormatter.GetColor(DataType.Unknown, text), IC10CodeFormatter.GetBackgroundColor(DataType.Unknown, text));
            }
        }
    }
}
