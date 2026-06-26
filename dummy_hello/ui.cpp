#include <windows.h>
#include <commctrl.h>

// 创建UI控件
void CreateUI(HWND hwnd, HINSTANCE hInstance) {
    // 创建标签（显示"唐诗欣赏"）
    HWND hLabel = CreateWindowW(
        L"STATIC",
        L"唐诗欣赏",
        WS_CHILD | WS_VISIBLE | SS_CENTER,
        300, 20, 200, 30,
        hwnd,
        NULL,
        hInstance,
        NULL
    );
    // 设置标签字体
    HFONT hLabelFont = CreateFontW(28, 0, 0, 0, FW_BOLD, FALSE, FALSE, FALSE,
                                   DEFAULT_CHARSET, OUT_DEFAULT_PRECIS, CLIP_DEFAULT_PRECIS,
                                   DEFAULT_QUALITY, DEFAULT_PITCH | FF_DONTCARE, L"SimHei");
    SendMessage(hLabel, WM_SETFONT, (WPARAM)hLabelFont, TRUE);

    // 创建多行文本框显示唐诗（调整位置和大小，使布局居中美观）
    HWND hEdit = CreateWindowW(
        L"EDIT",
        L"静夜思\r\n李白\r\n床前明月光，\r\n疑是地上霜。\r\n举头望明月，\r\n低头思故乡。",
        WS_CHILD | WS_VISIBLE | ES_MULTILINE | ES_READONLY | WS_VSCROLL | ES_AUTOVSCROLL | ES_CENTER,
        100, 70, 600, 350,
        hwnd,
        NULL,
        hInstance,
        NULL
    );
    // 设置文本框字体为宋体，大小适中
    HFONT hFont = CreateFontW(28, 0, 0, 0, FW_NORMAL, FALSE, FALSE, FALSE,
                              DEFAULT_CHARSET, OUT_DEFAULT_PRECIS, CLIP_DEFAULT_PRECIS,
                              DEFAULT_QUALITY, DEFAULT_PITCH | FF_DONTCARE, L"SimSun");
    SendMessage(hEdit, WM_SETFONT, (WPARAM)hFont, TRUE);

    // 创建关闭按钮
    HWND hCloseBtn = CreateWindowW(
        L"BUTTON",
        L"关闭",
        WS_CHILD | WS_VISIBLE | BS_PUSHBUTTON | WS_TABSTOP,
        350, 500, 100, 40,
        hwnd,
        (HMENU)2,
        hInstance,
        NULL
    );

    // 创建工具栏
    HWND hToolbar = CreateWindowExW(
        0,
        TOOLBARCLASSNAMEW,
        NULL,
        WS_CHILD | WS_VISIBLE | TBSTYLE_FLAT | CCS_TOP,
        0, 0, 0, 0,
        hwnd,
        NULL,
        hInstance,
        hInstance,
        NULL
    );

    // 设置工具栏颜色方案，使背景色与下方区域区分
    COLORSCHEME cs;
    cs.dwSize = sizeof(COLORSCHEME);
    cs.clrBtnHighlight = RGB(240, 240, 240);  // 浅灰色背景
    cs.clrBtnShadow = RGB(200, 200, 200);
    SendMessageW(hToolbar, TB_SETCOLORSCHEME, 0, (LPARAM)&cs);
    
    // 创建图像列表并添加位图
    HIMAGELIST hImageList = ImageList_Create(16, 16, ILC_COLOR32, 3, 0);
    cs.clrBtnHighlight = RGB(240, 240, 240);  // 浅灰色背景
    cs.clrBtnShadow = RGB(200, 200, 200);
    SendMessageW(hToolbar, TB_SETCOLORSCHEME, 0, (LPARAM)&cs);
    
    // 创建图像列表并添加位图
    HIMAGELIST hImageList = ImageList_Create(16, 16, ILC_COLOR32, 3, 0);
    
    // 创建简单位图作为图标（使用系统标准图标）
    HICON hIconOpen = LoadIcon(NULL, IDI_APPLICATION);
    HICON hIconSave = LoadIcon(NULL, IDI_APPLICATION);
    HICON hIconExit = LoadIcon(NULL, IDI_APPLICATION);
    
    ImageList_AddIcon(hImageList, hIconOpen);
    ImageList_AddIcon(hImageList, hIconSave);
    ImageList_AddIcon(hImageList, hIconExit);
    
    SendMessage(hToolbar, TB_SETIMAGELIST, 0, (LPARAM)hImageList);
    
    // 添加字符串到工具栏字符串池（Unicode）
    int idxOpen = (int)SendMessageW(hToolbar, TB_ADDSTRINGW, 0, (LPARAM)L"打开");
    int idxSave = (int)SendMessageW(hToolbar, TB_ADDSTRINGW, 0, (LPARAM)L"保存");
    int idxExit = (int)SendMessageW(hToolbar, TB_ADDSTRINGW, 0, (LPARAM)L"退出");
    
    // 设置工具栏按钮
    TBBUTTON tbButtons[3] = {};
    tbButtons[0].iBitmap = 0;
    tbButtons[0].idCommand = 100;
    tbButtons[0].fsState = TBSTATE_ENABLED;
    tbButtons[0].fsStyle = TBSTYLE_BUTTON | TBSTYLE_AUTOSIZE;
    tbButtons[0].iString = idxOpen;

    tbButtons[1].iBitmap = 1;
    tbButtons[1].idCommand = 101;
    tbButtons[1].fsState = TBSTATE_ENABLED;
    tbButtons[1].fsStyle = TBSTYLE_BUTTON | TBSTYLE_AUTOSIZE;
    tbButtons[1].iString = idxSave;

    tbButtons[2].iBitmap = 2;
    tbButtons[2].idCommand = 102;
    tbButtons[2].fsState = TBSTATE_ENABLED;
    tbButtons[2].fsStyle = TBSTYLE_BUTTON | TBSTYLE_AUTOSIZE;
    tbButtons[2].iString = idxExit;
    
    SendMessageW(hToolbar, TB_BUTTONSTRUCTSIZE, sizeof(TBBUTTON), 0);
    SendMessageW(hToolbar, TB_ADDBUTTONS, 3, (LPARAM)&tbButtons);
    SendMessageW(hToolbar, TB_SETSTYLE, 0, (LPARAM)(SendMessageW(hToolbar, TB_GETSTYLE, 0, 0) | TBSTYLE_LIST));
    SendMessageW(hToolbar, TB_AUTOSIZE, 0, 0);
    
    // 调整控件位置，为工具栏留出空间
    RECT rcToolbar;
    GetWindowRect(hToolbar, &rcToolbar);
    int toolbarHeight = rcToolbar.bottom - rcToolbar.top;
    
    // 更新标签位置
    SetWindowPos(hLabel, NULL, 300, 20 + toolbarHeight, 200, 30, SWP_NOZORDER);
    // 更新文本框位置
    SetWindowPos(hEdit, NULL, 100, 70 + toolbarHeight, 600, 350, SWP_NOZORDER);
    // 更新按钮位置
    SetWindowPos(hCloseBtn, NULL, 350, 500 + toolbarHeight, 100, 40, SWP_NOZORDER);
}
