import subprocess
import json
import os

# 测试用的 JSON
test_json = """
{
  "task": "创建Hello World C++程序",
  "changes": [
    {
      "file": "main.cpp",
      "ops": [
        {
          "type": "insert",
          "after": 0,
          "content": "#include <iostream>\\n\\nint main() {\\n    std::cout << \"Hello, World!\" << std::endl;\\n    return 0;\\n}"
        }
      ]
    }
  ]
}
"""

# 创建临时测试目录
test_dir = "test_new_file"
if not os.path.exists(test_dir):
    os.makedirs(test_dir)

# 写入测试 JSON
json_path = os.path.join(test_dir, "test.json")
with open(json_path, 'w', encoding='utf-8') as f:
    f.write(test_json)

print("测试 JSON 已创建:", json_path)
print("\n测试内容:")
print(test_json)

# 运行程序测试
print("\n" + "="*50)
print("测试完成，请在 MyIDE 中打开目录:", os.path.abspath(test_dir))
print("然后将上述 JSON 粘贴到 AI 返回标签页，点击应用按钮")
print("应该会自动创建 main.cpp 文件")
