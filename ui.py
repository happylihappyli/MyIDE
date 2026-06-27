import os
import queue
import subprocess
import sys
import threading
import tkinter as tk
from dataclasses import dataclass
from pathlib import Path
from tkinter import messagebox, ttk


@dataclass(frozen=True)
class ProjectTarget:
    """描述一个可编译、可启动的目标项目。"""

    key: str
    name: str
    project_dir: Path
    project_file: Path
    framework: str
    exe_name: str


class BuildLauncherUI:
    """用于编译并启动 MyIDE 和 MyChrome 的简易桌面界面。"""

    def __init__(self, root: tk.Tk) -> None:
        """初始化窗口、路径、项目配置和界面控件。"""
        self.root = root
        self.project_dir = Path(__file__).resolve().parent
        self.workspace_root = self.project_dir.parent.parent
        self.log_queue: "queue.Queue[str]" = queue.Queue()
        self.process: subprocess.Popen[str] | None = None
        self.is_building = False

        self.projects = self._build_project_targets()
        self.project_file_vars: dict[str, tk.StringVar] = {}
        self.exe_path_vars: dict[str, tk.StringVar] = {}
        self.build_buttons: dict[str, ttk.Button] = {}
        self.run_buttons: dict[str, ttk.Button] = {}
        self.open_buttons: dict[str, ttk.Button] = {}

        self.config_var = tk.StringVar(value="Release")
        self.status_var = tk.StringVar(value="就绪")

        self._build_ui()
        self._refresh_exe_paths()
        self._schedule_log_flush()

    def _build_project_targets(self) -> dict[str, ProjectTarget]:
        """构建需要在界面中管理的项目清单。"""
        mychrome_dir = self.workspace_root / "python" / "my_chrome" / "MyWebView2Browser"
        return {
            "myide": ProjectTarget(
                key="myide",
                name="MyIDE",
                project_dir=self.project_dir,
                project_file=self.project_dir / "MyIDE.csproj",
                framework="net8.0-windows",
                exe_name="MyIDE.exe",
            ),
            "mychrome": ProjectTarget(
                key="mychrome",
                name="MyChrome",
                project_dir=mychrome_dir,
                project_file=mychrome_dir / "MyWebView2Browser.csproj",
                framework="net9.0-windows",
                exe_name="MyChrome.exe",
            ),
        }

    def _build_ui(self) -> None:
        """创建主窗口中的配置区、项目卡片和日志区。"""
        self.root.title("MyIDE / MyChrome 编译启动工具")
        self.root.geometry("980x700")
        self.root.minsize(820, 560)

        container = ttk.Frame(self.root, padding=12)
        container.pack(fill=tk.BOTH, expand=True)

        config_card = ttk.LabelFrame(container, text="公共配置", padding=12)
        config_card.pack(fill=tk.X)

        ttk.Label(config_card, text="编译配置:").grid(row=0, column=0, sticky="w")
        self.config_combo = ttk.Combobox(
            config_card,
            textvariable=self.config_var,
            values=("Release", "Debug"),
            state="readonly",
            width=14,
        )
        self.config_combo.grid(row=0, column=1, sticky="w", padx=(8, 12))
        self.config_combo.bind("<<ComboboxSelected>>", lambda _e: self._refresh_exe_paths())
        ttk.Label(config_card, text="当前配置会同时影响 MyIDE 和 MyChrome 的编译与启动路径。").grid(
            row=0, column=2, sticky="w"
        )

        project_container = ttk.Frame(container, padding=(0, 10, 0, 0))
        project_container.pack(fill=tk.X)

        self._create_project_card(project_container, self.projects["myide"]).pack(fill=tk.X)
        self._create_project_card(project_container, self.projects["mychrome"]).pack(fill=tk.X, pady=(10, 0))

        status_bar = ttk.Frame(container, padding=(0, 10, 0, 10))
        status_bar.pack(fill=tk.X)
        ttk.Label(status_bar, text="状态:").pack(side=tk.LEFT)
        ttk.Label(status_bar, textvariable=self.status_var).pack(side=tk.LEFT, padx=(6, 0))

        log_card = ttk.LabelFrame(container, text="实时日志", padding=8)
        log_card.pack(fill=tk.BOTH, expand=True)

        self.log_text = tk.Text(
            log_card,
            wrap="word",
            bg="#1e1e1e",
            fg="#d4d4d4",
            insertbackground="#d4d4d4",
            font=("Consolas", 10),
            relief=tk.FLAT,
        )
        self.log_text.pack(side=tk.LEFT, fill=tk.BOTH, expand=True)

        scroll = ttk.Scrollbar(log_card, orient="vertical", command=self.log_text.yview)
        scroll.pack(side=tk.RIGHT, fill=tk.Y)
        self.log_text.configure(yscrollcommand=scroll.set)

        action_bar = ttk.Frame(container, padding=(0, 10, 0, 0))
        action_bar.pack(fill=tk.X)
        ttk.Button(action_bar, text="清空日志", command=self.clear_log).pack(side=tk.LEFT)
        ttk.Button(action_bar, text="退出", command=self.root.destroy).pack(side=tk.RIGHT)

    def _create_project_card(self, parent: ttk.Frame, target: ProjectTarget) -> ttk.LabelFrame:
        """为单个项目创建一张卡片，包含编译、启动和打开目录入口。"""
        card = ttk.LabelFrame(parent, text=target.name, padding=12)

        project_file_var = tk.StringVar(value=str(target.project_file))
        exe_path_var = tk.StringVar()
        self.project_file_vars[target.key] = project_file_var
        self.exe_path_vars[target.key] = exe_path_var

        ttk.Label(card, text="项目文件:").grid(row=0, column=0, sticky="w")
        ttk.Entry(card, state="readonly", width=90, justify="left", textvariable=project_file_var).grid(
            row=0, column=1, columnspan=4, sticky="ew", padx=(8, 0)
        )

        ttk.Button(card, text="编译", command=lambda key=target.key: self.build_project(key)).grid(
            row=1, column=1, sticky="w", pady=(10, 0)
        )
        ttk.Button(card, text="启动程序", command=lambda key=target.key: self.launch_program(key)).grid(
            row=1, column=2, sticky="w", padx=(8, 0), pady=(10, 0)
        )
        ttk.Button(card, text="打开输出目录", command=lambda key=target.key: self.open_output_dir(key)).grid(
            row=1, column=3, sticky="w", padx=(8, 0), pady=(10, 0)
        )

        self.build_buttons[target.key] = card.grid_slaves(row=1, column=1)[0]
        self.run_buttons[target.key] = card.grid_slaves(row=1, column=2)[0]
        self.open_buttons[target.key] = card.grid_slaves(row=1, column=3)[0]

        ttk.Label(card, text="程序路径:").grid(row=2, column=0, sticky="w", pady=(10, 0))
        ttk.Entry(card, state="readonly", textvariable=exe_path_var).grid(
            row=2, column=1, columnspan=4, sticky="ew", padx=(8, 0), pady=(10, 0)
        )

        card.columnconfigure(1, weight=1)
        return card

    def _refresh_exe_paths(self) -> None:
        """根据当前配置刷新所有项目对应的可执行文件路径。"""
        for key, target in self.projects.items():
            exe_path = target.project_dir / "bin" / self.config_var.get() / target.framework / target.exe_name
            self.exe_path_vars[key].set(str(exe_path))

    def _append_log(self, text: str) -> None:
        """把一条文本追加到日志框并自动滚动到底部。"""
        self.log_text.insert(tk.END, text)
        self.log_text.see(tk.END)

    def _schedule_log_flush(self) -> None:
        """定时把后台线程收集到的日志刷新到前台文本框。"""
        while True:
            try:
                line = self.log_queue.get_nowait()
            except queue.Empty:
                break
            self._append_log(line)
        self.root.after(100, self._schedule_log_flush)

    def _set_busy(self, busy: bool) -> None:
        """切换界面忙碌状态，避免重复触发编译。"""
        self.is_building = busy
        state = tk.DISABLED if busy else tk.NORMAL
        for button in self.build_buttons.values():
            button.config(state=state)
        self.config_combo.config(state="disabled" if busy else "readonly")

    def _get_target(self, project_key: str) -> ProjectTarget:
        """根据项目键名获取目标配置。"""
        return self.projects[project_key]

    def clear_log(self) -> None:
        """清空当前日志显示区域。"""
        self.log_text.delete("1.0", tk.END)

    def build_project(self, project_key: str) -> None:
        """启动后台线程执行指定项目的 dotnet build。"""
        if self.is_building:
            messagebox.showinfo("提示", "当前正在编译，请稍候。")
            return

        target = self._get_target(project_key)
        if not target.project_file.exists():
            messagebox.showerror("错误", f"未找到项目文件:\n{target.project_file}")
            return

        self._refresh_exe_paths()
        self._set_busy(True)
        self.status_var.set(f"正在编译 {target.name} ({self.config_var.get()}) ...")
        self.log_queue.put(f"\n=== 开始编译 {target.name}: {self.config_var.get()} ===\n")

        worker = threading.Thread(target=self._run_build, args=(project_key,), daemon=True)
        worker.start()

    def _run_build(self, project_key: str) -> None:
        """在后台线程中执行指定项目编译，并把输出逐行写入日志队列。"""
        target = self._get_target(project_key)
        command = [
            "dotnet",
            "build",
            str(target.project_file),
            "-c",
            self.config_var.get(),
        ]

        try:
            self.process = subprocess.Popen(
                command,
                cwd=target.project_dir,
                stdout=subprocess.PIPE,
                stderr=subprocess.STDOUT,
                text=True,
                encoding="utf-8",
                errors="replace",
            )

            assert self.process.stdout is not None
            for line in self.process.stdout:
                self.log_queue.put(f"[{target.name}] {line}")

            exit_code = self.process.wait()
            if exit_code == 0:
                self.log_queue.put(f"=== {target.name} 编译成功 ===\n")
                self.root.after(0, lambda: self.status_var.set(f"{target.name} 编译成功"))
            else:
                self.log_queue.put(f"=== {target.name} 编译失败，退出码: {exit_code} ===\n")
                self.root.after(0, lambda: self.status_var.set(f"{target.name} 编译失败"))
        except FileNotFoundError:
            self.log_queue.put("未找到 dotnet，请确认已安装 .NET SDK 并加入 PATH。\n")
            self.root.after(0, lambda: self.status_var.set("未找到 dotnet"))
        except Exception as ex:
            self.log_queue.put(f"{target.name} 编译过程中发生异常: {ex}\n")
            self.root.after(0, lambda: self.status_var.set(f"{target.name} 编译异常"))
        finally:
            self.process = None
            self.root.after(0, lambda: self._set_busy(False))

    def launch_program(self, project_key: str) -> None:
        """启动指定项目当前配置对应的可执行文件。"""
        self._refresh_exe_paths()
        target = self._get_target(project_key)
        exe_path = Path(self.exe_path_vars[project_key].get())

        if not exe_path.exists():
            should_build = messagebox.askyesno(
                "提示",
                f"未找到可执行文件:\n{exe_path}\n\n是否先执行编译？",
            )
            if should_build:
                self.build_project(project_key)
            return

        try:
            subprocess.Popen([str(exe_path)], cwd=exe_path.parent)
            self.status_var.set(f"{target.name} 已启动")
            self.log_queue.put(f"已启动 {target.name}: {exe_path}\n")
        except Exception as ex:
            messagebox.showerror("错误", f"启动失败:\n{ex}")
            self.status_var.set(f"{target.name} 启动失败")

    def open_output_dir(self, project_key: str) -> None:
        """打开指定项目当前配置对应的输出目录。"""
        output_dir = Path(self.exe_path_vars[project_key].get()).parent
        output_dir.mkdir(parents=True, exist_ok=True)

        try:
            os.startfile(str(output_dir))
        except Exception as ex:
            messagebox.showerror("错误", f"打开目录失败:\n{ex}")


def main() -> int:
    """创建并运行主窗口。"""
    root = tk.Tk()
    ttk.Style().theme_use("clam")
    BuildLauncherUI(root)
    root.mainloop()
    return 0


if __name__ == "__main__":
    sys.exit(main())
