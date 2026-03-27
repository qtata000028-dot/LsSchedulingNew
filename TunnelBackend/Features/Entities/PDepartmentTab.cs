using System;
using System.Collections.Generic;

namespace TunnelBackend.Features.Entities;

public partial class PDepartmentTab
{
    public int Departmentid { get; set; }

    /// <summary>
    /// 部门编码
    /// </summary>
    public string DepNo { get; set; } = null!;

    public string DepName { get; set; } = null!;

    public string Departmentcode { get; set; } = null!;

    /// <summary>
    /// 部门名称
    /// </summary>
    public string Departmentname { get; set; } = null!;

    /// <summary>
    /// 负责人
    /// </summary>
    public string DepScmaffimer { get; set; } = null!;

    public string DepRemark { get; set; } = null!;

    public int DepSign { get; set; }

    public int DepOrderid { get; set; }

    /// <summary>
    /// 联系电话
    /// </summary>
    public string DepPhone { get; set; } = null!;

    /// <summary>
    /// 经理
    /// </summary>
    public string? DepManager { get; set; }

    public string? Zzjgdm { get; set; }

    public string? DepPermissions { get; set; }

    public string? DepSpeciesNo { get; set; }

    public int? Orderid { get; set; }

    public int? DepWorkshopid { get; set; }

    public int? DepGroupdeparid { get; set; }

    public int? DepClientid { get; set; }

    public int? DepBusinessid { get; set; }

    public string? LskjimportErrorFlag { get; set; }

    public int? Groupid { get; set; }

    public string? DepManager1 { get; set; }

    public string? DepManager2 { get; set; }

    public DateTime? Jytime { get; set; }

    public int? DepartmentYjid { get; set; }

    public int? DepType { get; set; }

    public string? DepManager3 { get; set; }

    public string? Cengji { get; set; }

    public string? Bmclass { get; set; }

    public int? DepGroupid { get; set; }

    /// <summary>
    /// 主管
    /// </summary>
    public string? DepZhuguan { get; set; }

    /// <summary>
    /// 工会计提比例
    /// </summary>
    public string? DepGhjtbl { get; set; }

    public int? DepPx { get; set; }
}
