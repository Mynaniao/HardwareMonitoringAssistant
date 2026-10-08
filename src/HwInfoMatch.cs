// HwInfoMatch.cs —— HWiNFO 读数的多平台匹配规则引擎
//
// 为什么单独一个文件：GUI（HardwareMonitor.cs）与命令行测试台（tools/MatchHarness.cs）
// 共用同一份规则，这样规则可以拿真实转储数据做回归验证。
//
// 设计要点（来自 2026-10-08 在 Intel Core Ultra 9 290HX + RTX 5070 Ti Laptop 上的真实转储实测）：
//   * HWiNFO 读数标签是英文 szLabel，大小写不敏感匹配即可；
//   * **GPU 休眠时该组读数的标签是空字符串**、值恒为 0（只剩 max/avg 的历史值）→ 必须跳过空标签，
//     并让"独显无数据 → 回退核显"，否则笔记本待机时 GPU 行会显示假 0；
//   * 单位字段是 ANSI 字节（℃ = A1 E6、% = 25），不可靠 → 用**数值合理区间**校验，不靠单位；
//   * 实测标签词汇：
//       Intel 温度   CPU Package（Enhanced / DTS 组）、逐核 P-core N / E-core N
//       AMD   温度   CPU (Tctl/Tdie)、CPU (Tctl)、CPU (Tdie)、CPU Die (average)
//       使用率       Total CPU Usage（Intel/AMD 通用）
//       NVIDIA       GPU Core Load、GPU Temperature
//       Intel 核显   GPU Core Temperature、GPU Total Usage、GPU Computing Usage
//       AMD 显卡     GPU Core Load、GPU Temperature、GPU (D3D) Usage
//       硬盘         S.M.A.R.T. 组的 Drive Temperature、Drive: 组的 Total Activity
using System;
using System.Collections.Generic;

/// <summary>一条 HWiNFO 读数。</summary>
public class HwReading
{
    public string Sensor = "";
    public string Label = "";
    public string Unit = "";
    public double Value;
    public double Min;
    public double Max;
    public double Avg;

    public override string ToString()
    {
        return Sensor + " | " + Label + " | " + Value.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
    }
}

public static class HwMatch
{
    /// <summary>一条匹配规则。按数组顺序优先，第一条有命中的胜出。</summary>
    public struct Rule
    {
        public string SensorHas;    // 传感器组名需包含（空 = 不限）
        public string LabelIs;      // 标签精确等于（空 = 不限）
        public string LabelHas;     // 标签需包含（空 = 不限）
        public string LabelNot;     // 标签不能包含（多个用 | 分隔）
        public bool Temp;           // 只接受合理温度区间（0.5 ~ 130）
        public bool Pct;            // 只接受 0 ~ 100.5
        public bool Max;            // 多条命中取最大值（否则取第一条）
        public bool NonZero;        // 排除 0 值
    }

    static Rule R(string sensorHas, string labelIs, string labelHas, string labelNot, bool temp, bool pct, bool max, bool nonZero)
    {
        Rule r = new Rule();
        r.SensorHas = sensorHas; r.LabelIs = labelIs; r.LabelHas = labelHas; r.LabelNot = labelNot;
        r.Temp = temp; r.Pct = pct; r.Max = max; r.NonZero = nonZero;
        return r;
    }
    static Rule T(string sensorHas, string labelIs, string labelHas)
    { return R(sensorHas, labelIs, labelHas, "", true, false, false, true); }
    static Rule TMax(string sensorHas, string labelHas, string labelNot)
    { return R(sensorHas, "", labelHas, labelNot, true, false, true, true); }
    static Rule P(string sensorHas, string labelIs, string labelHas)
    { return R(sensorHas, labelIs, labelHas, "", false, true, false, true); }
    static Rule PMax(string sensorHas, string labelHas, string labelNot)
    { return R(sensorHas, "", labelHas, labelNot, false, true, true, true); }

    /// <summary>GPU 占用里那些"子单元"读数，兜底时必须排除，否则会把 EU/引擎占用当成整卡占用。</summary>
    const string GPU_SUB = "Computing|Memory|Video|Render|Copy|Media|Decode|Encode|Engine|EU|Blitter|Sampler|3D|Compute";

    static bool Has(string hay, string needle)
    {
        return !string.IsNullOrEmpty(hay) && hay.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;
    }
    static bool HasAny(string hay, string pipes)
    {
        if (string.IsNullOrEmpty(pipes)) return false;
        foreach (string p in pipes.Split('|'))
            if (Has(hay, p.Trim())) return true;
        return false;
    }
    static bool Ok(Rule r, HwReading x)
    {
        if (string.IsNullOrEmpty(x.Label)) return false;                        // 关键：GPU 休眠时标签为空
        if (!string.IsNullOrEmpty(r.SensorHas) && !Has(x.Sensor, r.SensorHas)) return false;
        if (!string.IsNullOrEmpty(r.LabelIs) && !x.Label.Trim().Equals(r.LabelIs, StringComparison.OrdinalIgnoreCase)) return false;
        if (!string.IsNullOrEmpty(r.LabelHas) && !Has(x.Label, r.LabelHas)) return false;
        if (HasAny(x.Label, r.LabelNot)) return false;
        if (r.Temp && !(x.Value > 0.5 && x.Value < 130)) return false;
        if (r.Pct && !(x.Value >= 0 && x.Value <= 100.5)) return false;
        if (r.NonZero && x.Value == 0) return false;
        return true;
    }

    /// <summary>按规则顺序挑一条读数；都没命中返回 null。</summary>
    public static HwReading Pick(List<HwReading> all, Rule[] rules)
    {
        int ri, n;
        return Pick(all, rules, out ri, out n);
    }

    /// <summary>带诊断：ruleIndex = 命中的规则序号（-1 = 全未命中），matches = 该规则命中条数。</summary>
    public static HwReading Pick(List<HwReading> all, Rule[] rules, out int ruleIndex, out int matches)
    {
        ruleIndex = -1;
        matches = 0;
        for (int ri = 0; ri < rules.Length; ri++)
        {
            Rule r = rules[ri];
            HwReading best = null;
            int cnt = 0;
            foreach (HwReading x in all)
            {
                if (!Ok(r, x)) continue;
                cnt++;
                if (best == null) best = x;
                else if (r.Max && x.Value > best.Value) best = x;
            }
            if (best != null) { ruleIndex = ri; matches = cnt; return best; }
        }
        return null;
    }

    // ===================== CPU =====================

    public static readonly Rule[] CpuTempRules = new Rule[]
    {
        T("Enhanced", "CPU Package", ""),          // Intel 现代（Enhanced 组）
        T("CPU", "CPU Package", ""),               // Intel 传统（DTS 组）
        T("CPU", "", "CPU (Tctl/Tdie)"),           // AMD Zen 最典型
        T("CPU", "", "Tctl/Tdie"),
        T("CPU", "", "CPU (Tctl)"),
        T("CPU", "", "CPU (Tdie)"),
        T("CPU", "", "CPU Die (average)"),
        T("CPU", "", "CPU Package"),
        T("CPU", "", "CPU Temperature"),
        T("CPU", "", "CPU Temp"),
        R("CPU", "", "CPU", "core", true, false, false, true),      // 泛化：含 CPU、排除逐核
        TMax("CPU", "", "core"),                                    // 兜底：逐核最大值（P-core / E-core）
        TMax("CPU", "", ""),                                        // 最后兜底：该组所有温度取最大
    };

    /// <summary>CPU 温度（℃）；取不到返回 NaN。</summary>
    public static double CpuTemp(List<HwReading> all)
    {
        HwReading r = Pick(all, CpuTempRules);
        return r == null ? double.NaN : r.Value;
    }

    public static readonly Rule[] CpuUsageRules = new Rule[]
    {
        P("CPU", "Total CPU Usage", ""),            // Intel / AMD 通用
        P("", "Total CPU Usage", ""),
        P("CPU", "", "Total CPU"),
        P("CPU", "", "CPU Total"),
        P("CPU", "", "CPU Utilization"),
        P("CPU", "", "CPU Usage"),
        PMax("CPU", "", "core"),                    // 兜底：逐核使用率取最大
    };

    /// <summary>CPU 总占用（%）；取不到返回 NaN（调用方可回退到 PDH 计数器）。</summary>
    public static double CpuUsage(List<HwReading> all)
    {
        HwReading r = Pick(all, CpuUsageRules);
        return r == null ? double.NaN : r.Value;
    }

    // ===================== GPU =====================

    public static readonly Rule[] GpuTempRulesD = new Rule[]
    {
        T("dGPU", "GPU Temperature", ""),
        T("dGPU", "GPU Core Temperature", ""),
        T("dGPU", "", "GPU Temperature"),
        TMax("dGPU", "Temperature", "Hot Spot|Memory|VRAM|Junction|Board|Fan"),
    };
    public static readonly Rule[] GpuTempRulesAny = new Rule[]
    {
        T("", "GPU Temperature", ""),
        T("", "GPU Core Temperature", ""),
        T("", "GPU Temp", ""),
        R("GPU", "", "Temperature", "Hot Spot|Memory|VRAM|Junction|Board|Fan|Cores|CPU", true, false, true, true),
    };

    /// <summary>GPU 温度（℃）：优先独显；独显无数据（笔记本休眠很常见）回退核显；取不到返回 NaN。</summary>
    public static double GpuTemp(List<HwReading> all)
    {
        HwReading r = Pick(all, GpuTempRulesD);
        if (r == null) r = Pick(all, GpuTempRulesAny);
        return r == null ? double.NaN : r.Value;
    }

    public static readonly Rule[] GpuUsageRulesD = new Rule[]
    {
        P("dGPU", "GPU Core Load", ""),             // NVIDIA 典型
        P("dGPU", "GPU Total Usage", ""),
        P("dGPU", "", "GPU Core Load"),
        P("dGPU", "", "GPU Total Usage"),
        P("dGPU", "", "GPU Utilization"),
        P("dGPU", "", "GPU D3D Usage"),
        P("dGPU", "", "GPU Usage"),
        P("dGPU", "", "GPU Load"),
        PMax("dGPU", "", "Usage|Load"),
    };
    public static readonly Rule[] GpuUsageRulesAny = new Rule[]
    {
        P("", "GPU Core Load", ""),
        P("", "GPU Total Usage", ""),
        P("", "", "GPU Core Load"),
        P("", "", "GPU Total Usage"),
        P("", "", "GPU Utilization"),
        P("", "", "GPU D3D Usage"),
        P("", "", "GPU Usage"),
        P("", "", "GPU Load"),
        PMax("GPU", "", "Usage|Load"),
    };

    /// <summary>GPU 占用（%）：优先独显，其次任意 GPU 组；取不到返回 NaN。</summary>
    public static double GpuUsage(List<HwReading> all)
    {
        HwReading r = Pick(all, GpuUsageRulesD);
        if (r == null) r = Pick(all, GpuUsageRulesAny);
        return r == null ? double.NaN : r.Value;
    }

    public static readonly Rule[] VramUsedRules = new Rule[]
    {
        R("", "", "D3D Dedicated Memory Used", "", false, false, false, true),
        R("", "", "GPU Memory Allocated", "", false, false, false, true),
        R("", "", "GPU Memory Used", "", false, false, false, true),
        R("", "", "Dedicated Memory Used", "", false, false, false, true),
    };

    /// <summary>显存已用（MB）：来自 HWiNFO 的显存读数；取不到返回 NaN。</summary>
    public static double VramUsedMb(List<HwReading> all)
    {
        HwReading r = Pick(all, VramUsedRules);
        return r == null ? double.NaN : r.Value;
    }

    public static readonly Rule[] VramPctRules = new Rule[]
    {
        R("", "", "GPU Memory Usage", "Limit|Shared|Budget|Reserved", false, true, true, true),
    };

    /// <summary>显存占用率（%）：用于在拿不到 MB 时按总量折算；取不到返回 NaN。</summary>
    public static double VramUsagePct(List<HwReading> all)
    {
        HwReading r = Pick(all, VramPctRules);
        return r == null ? double.NaN : r.Value;
    }

    // ===================== 硬盘 =====================

    public static readonly Rule[] DiskTempRules = new Rule[]
    {
        TMax("S.M.A.R.T", "Drive Temperature", ""),
    };

    /// <summary>硬盘温度（℃）：所有物理盘 Drive Temperature 的最大值。</summary>
    public static double DiskTemp(List<HwReading> all)
    {
        HwReading r = Pick(all, DiskTempRules);
        return r == null ? double.NaN : r.Value;
    }

    public static readonly Rule[] DiskActRules = new Rule[]
    {
        PMax("Drive:", "Total Activity", ""),
    };

    /// <summary>硬盘活动率（%）：所有 Drive Total Activity 的最大值。</summary>
    public static double DiskActivity(List<HwReading> all)
    {
        HwReading r = Pick(all, DiskActRules);
        return r == null ? double.NaN : r.Value;
    }

    // ===================== 显示 =====================

    /// <summary>数值格式化：NaN / 无穷 → "--"，避免界面出现 "NaN%"。</summary>
    public static string Fmt(double v, string format, string suffix)
    {
        if (double.IsNaN(v) || double.IsInfinity(v)) return "--";
        return v.ToString(format, System.Globalization.CultureInfo.InvariantCulture) + suffix;
    }
}
