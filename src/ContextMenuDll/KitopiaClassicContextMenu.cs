using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using ContextMenuDll.Interop;

namespace ContextMenuDll;

// Supplies the classic menu's COM identity, including its Debug comhost mapping.
#if !DEBUG
[GeneratedComClass]
#endif
[ComVisible(true)]
[Guid("CF88EA19-88F4-4C8A-9F8C-F593CE61D52D")]
[ComDefaultInterface(typeof(IContextMenu))]
public partial class KitopiaClassicContextMenu : KitopiaExplorerCommand, IShellExtInit, IContextMenu;
