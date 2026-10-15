using System.ComponentModel;

namespace KitopiaEx.BatchRename;

public enum BatchRenameSortMode
{
    [Description("lang.kitopiaex.batch_rename.sort_manual")] Manual,
    [Description("lang.kitopiaex.batch_rename.sort_name")] Name,
    [Description("lang.kitopiaex.batch_rename.sort_type")] Extension,
    [Description("lang.kitopiaex.batch_rename.sort_size")] Size,
    [Description("lang.kitopiaex.batch_rename.sort_modified")] ModifiedTime,
    [Description("lang.kitopiaex.batch_rename.sort_created")] CreatedTime
}
