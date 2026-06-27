你是一个代码修改助手。请根据【任务说明】修改【项目】中的文件，并优先使用 Patch 协议返回代码改动。
继续沿用当前 Session 已约定的 Patch 协议，本次不要重复解释协议内容。
仍然优先返回 Patch 修改区段，并把命令单独放进【命令】区段中的 JSON 数组。

【任务说明】
保存文件的时候闪退，增加输出日志，方便查找问题

【本次优先处理的文件】
Controls/DesignCanvas.cs
Controls/NodeTreePanel.cs
Controls/PropertyGrid.cs
Controls/ToolboxControl.cs
Models/DesignDocument.cs
Models/HtmlElement.cs
MainForm.cs
Program.cs

【项目环境与约束】
项目根目录: E:\GitHub3\csharp\html_design

【当前关注的文件】
--- Controls/DesignCanvas.cs ---
```
  1: using HtmlDesigner.Models;
  2: using System.Drawing.Drawing2D;
  3: 
  4: namespace HtmlDesigner.Controls;
  5: 
  6: public class DesignCanvas : Panel
  7: {
  8:     private Models.DesignDocument _document;
  9:     private Models.DesignHtmlElement? _selectedElement;
 10:     private bool _isDragging;
 11:     private Point _dragOffset;
 12:     private bool _isResizing;
 13:     private int _resizeHandle = -1;
 14:     private const int HandleSize = 8;
 15: 
 16:     public event Action<Models.DesignHtmlElement?>? ElementSelectedChanged;
 17:     public event Action? ElementsChanged;
 18: 
 19:     public Models.DesignDocument Document
 20:     {
 21:         get => _document;
 22:         set
 23:         {
 24:             _document = value;
 25:             _selectedElement = null;
 26:             Invalidate();
 27:         }
 28:     }
 29: 
 30:     public Models.DesignHtmlElement? SelectedElement
 31:     {
 32:         get => _selectedElement;
 33:         set
 34:         {
 35:             _selectedElement = value;
 36:             ElementSelectedChanged?.Invoke(_selectedElement);
 37:             Invalidate();
 38:         }
 39:     }
 40: 
 41:     public DesignCanvas()
 42:     {
 43:         _document = new Models.DesignDocument();
 44:         DoubleBuffered = true;
 45:         BackColor = Color.White;
 46:         BorderStyle = BorderStyle.FixedSingle;
 47: 
 48:         MouseDown += OnMouseDown;
 49:         MouseMove += OnMouseMove;
 50:         MouseUp += OnMouseUp;
 51:         Paint += OnPaint;
 52:         KeyDown += OnKeyDown;
 53:     }
 54: 
 55:     private void OnKeyDown(object? sender, KeyEventArgs e)
 56:     {
 57:         if (e.KeyCode == Keys.Delete && _selectedElement != null)
 58:         {
 59:             RemoveSelectedElement();
 60:         }
 61:     }
 62: 
 63:     public void AddElement(string type)
 64:     {
 65:         try
 66:         {
 67:         string? parentId = null;
 68:         int baseX = 20, baseY = 20;
 69:         int offsetX = 0, offsetY = 0;
 70:         if (_selectedElement != null)
 71:         {
 72:             parentId = _selectedElement.Id;
 73:             offsetX = _selectedElement.X + 20;
 74:             offsetY = _selectedElement.Y + 30;
 75:             baseX = _selectedElement.X + 15;
 76:             baseY = _selectedElement.Y + 25;
 77:         }
 78:         var el = new Models.DesignHtmlElement
 79:         {
 80:             Type = type,
 81:             X = offsetX > 0 ? offsetX + (_document.Elements.Count * 10) % 100 : baseX,
 82:             Y = offsetY > 0 ? offsetY + (_document.Elements.Count * 10) % 80 : baseY,
 83:             ParentId = parentId,
 84:             Width = parentId != null ? 80 : type switch { "label" => 120, "checkbox" or "multicheck" => 180, "image" => 150, _ => 120 },
 85:             Height = parentId != null ? 28 : type switch { "image" => 150, _ => 36 },
 86:                 Text = type switch
 87:                 {
 88:                     "button" => "Button",
 89:                     "label" => "Label",
 90:                     "panel" => "Panel",
 91:                     "dropdown" => "Select",
 92:                     "checkbox" => "Checkbox",
 93:                     "multicheck" => "Multi-Check",
 94:                     "image" => "Image",
 95:                     _ => "Element"
 96:                 },
 97:                 BackgroundColor = type switch { "button" => "#4a90d9", "panel" => "#fafafa", "label" => "#ffffff", _ => "#ffffff" },
 98:                 ForegroundColor = type switch { "button" => "#ffffff", _ => "#333333" },
 99:                 ZIndex = _document.Elements.Count
100:             };
101:             _document.Elements.Add(el);
102:             ElementsChanged?.Invoke();
103:             SelectedElement = el;
104:             Invalidate();
105:         }
106:         catch (Exception ex)
107:         {
108:             File.AppendAllText("error.log", $"{DateTime.Now}: AddElement Error: {ex}\n");
109:         }
110:     }
111: 
112:     public void RemoveSelectedElement()
113:     {
114:         try
115:         {
116:             if (_selectedElement != null)
117:             {
118:                 _document.Elements.Remove(_selectedElement);
119:                 SelectedElement = null;
120:                 ElementsChanged?.Invoke();
121:                 Invalidate();
122:             }
123:         }
124:         catch (Exception ex)
125:         {
126:             File.AppendAllText("error.log", $"{DateTime.Now}: RemoveSelectedElement Error: {ex}\n");
127:         }
128:     }
129: 
130:     public void UpdateSelectedElement(Models.DesignHtmlElement updated)
131:     {
132:         if (_selectedElement != null)
133:         {
134:             var idx = _document.Elements.IndexOf(_selectedElement);
135:             if (idx >= 0)
136:             {
137:                 _document.Elements[idx] = updated;
138:                 SelectedElement = updated;
139:                 Invalidate();
140:             }
141:         }
142:     }
143: 
144:     private void OnMouseDown(object? sender, MouseEventArgs e)
145:     {
146:         if (_selectedElement != null)
147:         {
148:             int handle = HitTestHandle(_selectedElement, e.Location);
149:             if (handle >= 0)
150:             {
151:                 _isResizing = true;
152:                 _resizeHandle = handle;
153:                 return;
154:             }
155:         }
156: 
157:         for (int i = _document.Elements.Count - 1; i >= 0; i--)
158:         {
159:             var el = _document.Elements[i];
160:             var rect = new Rectangle(el.X, el.Y, el.Width, el.Height);
161:             if (rect.Contains(e.Location))
162:             {
163:                 SelectedElement = el;
164:                 _isDragging = true;
165:                 _dragOffset = new Point(e.X - el.X, e.Y - el.Y);
166:                 Cursor = Cursors.SizeAll;
167:                 return;
168:             }
169:         }
170:         SelectedElement = null;
171:         Cursor = Cursors.Default;
172:     }
173: 
174:     private void OnMouseMove(object? sender, MouseEventArgs e)
175:     {
176:         if (_isResizing && _selectedElement != null)
177:         {
178:             ResizeElement(_selectedElement, e.Location);
179:             Invalidate();
180:             return;
181:         }
182: 
183:         if (_isDragging && _selectedElement != null)
184:         {
185:             _selectedElement.X = e.X - _dragOffset.X;
186:             _selectedElement.Y = e.Y - _dragOffset.Y;
187:             _selectedElement.ZIndex = _document.Elements.Count;
188:             _document.Elements.Sort((a, b) => a.ZIndex.CompareTo(b.ZIndex));
189:             Invalidate();
190:             return;
191:         }
192: 
193:         if (_selectedElement != null)
194:         {
195:             int handle = HitTestHandle(_selectedElement, e.Location);
196:             Cursor = handle switch
197:             {
198:                 0 or 4 => Cursors.SizeNWSE,
199:                 2 or 6 => Cursors.SizeNESW,
200:                 1 or 5 => Cursors.SizeNS,
201:                 3 or 7 => Cursors.SizeWE,
202:                 _ => Cursors.Default
203:             };
204:         }
205:     }
206: 
207:     private void OnMouseUp(object? sender, MouseEventArgs e)
208:     {
209:         _isDragging = false;
210:         _isResizing = false;
211:         _resizeHandle = -1;
212:         Cursor = Cursors.Default;
213:         ElementsChanged?.Invoke();
214:         Invalidate();
215:     }
216: 
217:     private int HitTestHandle(Models.DesignHtmlElement el, Point pt)
218:     {
219:         var rects = new Rectangle[]
220:         {
221:             new Rectangle(el.X - HandleSize/2, el.Y - HandleSize/2, HandleSize, HandleSize),
222:             new Rectangle(el.X + el.Width/2 - HandleSize/2, el.Y - HandleSize/2, HandleSize, HandleSize),
223:             new Rectangle(el.X + el.Width - HandleSize/2, el.Y - HandleSize/2, HandleSize, HandleSize),
224:             new Rectangle(el.X + el.Width - HandleSize/2, el.Y + el.Height/2 - HandleSize/2, HandleSize, HandleSize),
225:             new Rectangle(el.X + el.Width - HandleSize/2, el.Y + el.Height - HandleSize/2, HandleSize, HandleSize),
226:             new Rectangle(el.X + el.Width/2 - HandleSize/2, el.Y + el.Height - HandleSize/2, HandleSize, HandleSize),
227:             new Rectangle(el.X - HandleSize/2, el.Y + el.Height - HandleSize/2, HandleSize, HandleSize),
228:             new Rectangle(el.X - HandleSize/2, el.Y + el.Height/2 - HandleSize/2, HandleSize, HandleSize),
229:         };
230:         for (int i = 0; i < rects.Length; i++)
231:         {
232:             if (rects[i].Contains(pt)) return i;
233:         }
234:         return -1;
235:     }
236: 
237:     private void ResizeElement(Models.DesignHtmlElement el, Point pt)
238:     {
239:         int minW = 30, minH = 20;
240:         switch (_resizeHandle)
241:         {
242:             case 0: el.Width = Math.Max(minW, el.X + el.Width - pt.X); el.X = pt.X; el.Height = Math.Max(minH, el.Y + el.Height - pt.Y); el.Y = pt.Y; break;
243:             case 1: el.Height = Math.Max(minH, el.Y + el.Height - pt.Y); el.Y = pt.Y; break;
244:             case 2: el.Width = Math.Max(minW, pt.X - el.X); el.Height = Math.Max(minH, el.Y + el.Height - pt.Y); el.Y = pt.Y; break;
245:             case 3: el.Width = Math.Max(minW, pt.X - el.X); break;
246:             case 4: el.Width = Math.Max(minW, pt.X - el.X); el.Height = Math.Max(minH, pt.Y - el.Y); break;
247:             case 5: el.Height = Math.Max(minH, pt.Y - el.Y); break;
248:             case 6: el.Width = Math.Max(minW, el.X + el.Width - pt.X); el.X = pt.X; el.Height = Math.Max(minH, pt.Y - el.Y); break;
249:             case 7: el.Width = Math.Max(minW, el.X + el.Width - pt.X); el.X = pt.X; break;
250:         }
251:     }
252: 
253:     private void OnPaint(object? sender, PaintEventArgs e)
254:     {
255:         var g = e.Graphics;
256:         g.SmoothingMode = SmoothingMode.AntiAlias;
257:         g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
258: 
259:         var sorted = _document.Elements.OrderBy(el => el.ZIndex).ToList();
260:         foreach (var el in sorted)
261:         {
262:             DrawElement(g, el);
263:         }
264: 
265:         if (_selectedElement != null)
266:         {
267:             using var pen = new Pen(Color.DodgerBlue, 2) { DashStyle = DashStyle.Dash };
268:             g.DrawRectangle(pen, _selectedElement.X - 2, _selectedElement.Y - 2,
269:                 _selectedElement.Width + 4, _selectedElement.Height + 4);
270: 
271:             using var handleBrush = new SolidBrush(Color.White);
272:             using var handlePen = new Pen(Color.DodgerBlue, 1);
273:             var handles = new Point[]
274:             {
275:                 new Point(_selectedElement.X, _selectedElement.Y),
276:                 new Point(_selectedElement.X + _selectedElement.Width/2, _selectedElement.Y),
277:                 new Point(_selectedElement.X + _selectedElement.Width, _selectedElement.Y),
278:                 new Point(_selectedElement.X + _selectedElement.Width, _selectedElement.Y + _selectedElement.Height/2),
279:                 new Point(_selectedElement.X + _selectedElement.Width, _selectedElement.Y + _selectedElement.Height),
280:                 new Point(_selectedElement.X + _selectedElement.Width/2, _selectedElement.Y + _selectedElement.Height),
281:                 new Point(_selectedElement.X, _selectedElement.Y + _selectedElement.Height),
282:                 new Point(_selectedElement.X, _selectedElement.Y + _selectedElement.Height/2),
283:             };
284:             foreach (var hp in handles)
285:             {
286:                 var hr = new Rectangle(hp.X - HandleSize/2, hp.Y - HandleSize/2, HandleSize, HandleSize);
287:                 g.FillRectangle(handleBrush, hr);
288:                 g.DrawRectangle(handlePen, hr);
289:             }
290:         }
291:     }
292: 
293:     private void DrawElement(Graphics g, Models.DesignHtmlElement el)
294:     {
295:         var bg = ColorTranslator.FromHtml(el.BackgroundColor);
296:         var fg = ColorTranslator.FromHtml(el.ForegroundColor);
297:         var rect = new Rectangle(el.X, el.Y, el.Width, el.Height);
298:         using var font = new Font("Segoe UI", el.FontSize, FontStyle.Regular);
299:         using var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
300: 
301:         switch (el.Type)
302:         {
303:             case "button":
304:                 {
305:                     using var path = RoundedRect(rect, 6);
306:                     using var brush = new SolidBrush(bg);
307:                     g.FillPath(brush, path);
308:                     using var borderPen = new Pen(ControlPaint.Dark(bg), 1);
309:                     g.DrawPath(borderPen, path);
310:                     using var lgBrush = new LinearGradientBrush(rect, Color.FromArgb(40, 255, 255, 255), Color.FromArgb(0, 255, 255, 255), LinearGradientMode.Vertical);
311:                     g.FillPath(lgBrush, path);
312:                 }
313:                 g.DrawString(el.Text, font, new SolidBrush(fg), rect, sf);
314:                 break;
315:             case "panel":
316:                 using (var brush = new SolidBrush(bg))
317:                     g.FillRectangle(brush, rect);
318:                 using (var borderPen = new Pen(Color.FromArgb(200, 200, 200), 2))
319:                     g.DrawRectangle(borderPen, rect);
320:                 using (var headerBrush = new SolidBrush(Color.FromArgb(230, 230, 230)))
321:                     g.FillRectangle(headerBrush, el.X, el.Y, el.Width, 24);
322:                 var headerRect = new Rectangle(el.X, el.Y, el.Width, 24);
323:                 g.DrawString(el.Text, new Font("Segoe UI", 10, FontStyle.Bold), new SolidBrush(fg), headerRect, sf);
324:                 break;
325:             case "label":
326:                 using (var leftSf = new StringFormat { Alignment = StringAlignment.Near, LineAlignment = StringAlignment.Center })
327:                     g.DrawString(el.Text, font, new SolidBrush(fg), rect, leftSf);
328:                 break;
329:             case "dropdown":
330:                 {
331:                     using var path = RoundedRect(rect, 4);
332:                     using var brush = new SolidBrush(bg);
333:                     g.FillPath(brush, path);
334:                     using var borderPen = new Pen(Color.FromArgb(180, 180, 180), 1);
335:                     g.DrawPath(borderPen, path);
336:                 }
337:                 var dropText = el.Options.Count > 0 ? el.Options[0] : el.Text;
338:                 using (var leftSf2 = new StringFormat { Alignment = StringAlignment.Near, LineAlignment = StringAlignment.Center })
339:                 {
340:                     var textRect2 = new Rectangle(el.X + 8, el.Y, el.Width - 30, el.Height);
341:                     g.DrawString(dropText, font, new SolidBrush(fg), textRect2, leftSf2);
342:                 }
343:                 int arrowX = el.X + el.Width - 22;
344:                 int arrowY = el.Y + el.Height / 2 - 4;
345:                 using (var arrowPen = new Pen(fg, 2) { StartCap = LineCap.Round, EndCap = LineCap.Round })
346:                 {
347:                     g.DrawLine(arrowPen, arrowX, arrowY, arrowX + 8, arrowY);
348:                     g.DrawLine(arrowPen, arrowX + 4, arrowY - 4, arrowX + 8, arrowY);
349:                     g.DrawLine(arrowPen, arrowX + 4, arrowY + 4, arrowX + 8, arrowY);
350:                 }
351:                 break;
352:             case "checkbox":
353:             case "multicheck":
354:                 var cbRect = new Rectangle(el.X + 6, el.Y + (el.Height - 20) / 2, 20, 20);
355:                 {
356:                     using var cbPath = RoundedRect(cbRect, 3);
357:                     using var cbBrush = new SolidBrush(Color.White);
358:                     g.FillPath(cbBrush, cbPath);
359:                     using var cbPen = new Pen(Color.FromArgb(160, 160, 160), 1.5f);
360:                     g.DrawPath(cbPen, cbPath);
361:                 }
362:                 if (el.Checked)
363:                 {
364:                     using var cbPath2 = RoundedRect(cbRect, 3);
365:                     using var checkBrush2 = new SolidBrush(Color.FromArgb(0, 120, 212));
366:                     g.FillPath(checkBrush2, cbPath2);
367:                     using var checkPen2 = new Pen(Color.White, 2) { StartCap = LineCap.Round, EndCap = LineCap.Round };
368:                     g.DrawLine(checkPen2, cbRect.Left + 4, cbRect.Top + 10, cbRect.Left + 8, cbRect.Bottom - 5);
369:                     g.DrawLine(checkPen2, cbRect.Left + 8, cbRect.Bottom - 5, cbRect.Right - 4, cbRect.Top + 3);
370:                 }
371:                 var cbTextRect = new Rectangle(el.X + 32, el.Y, el.Width - 32, el.Height);
372:                 using (var leftSf3 = new StringFormat { Alignment = StringAlignment.Near, LineAlignment = StringAlignment.Center })
373:                     g.DrawString(el.Text, font, new SolidBrush(fg), cbTextRect, leftSf3);
374:                 break;
375:             case "image":
376:                 using (var dashPen = new Pen(Color.FromArgb(180, 180, 180), 1) { DashStyle = DashStyle.Dash })
377:                 using (var imgBrush = new SolidBrush(Color.FromArgb(248, 248, 248)))
378:                 {
379:                     g.FillRectangle(imgBrush, rect);
380:                     g.DrawRectangle(dashPen, rect);
381:                 }
382:                 var imgIconRect = new Rectangle(el.X + el.Width/2 - 20, el.Y + el.Height/2 - 25, 40, 40);
383:                 using (var iconPen = new Pen(Color.FromArgb(180, 180, 180), 2))
384:                 {
385:                     g.DrawRectangle(iconPen, imgIconRect);
386:                     g.DrawLine(iconPen, imgIconRect.Left + 10, imgIconRect.Top + 15, imgIconRect.Left + 20, imgIconRect.Top + 5);
387:                     g.DrawLine(iconPen, imgIconRect.Left + 20, imgIconRect.Top + 5, imgIconRect.Right - 10, imgIconRect.Top + 15);
388:                     g.DrawEllipse(iconPen, imgIconRect.Left + 14, imgIconRect.Top + 18, 12, 12);
389:                 }
390:                 var imgLabelRect = new Rectangle(el.X, el.Y + el.Height - 22, el.Width, 20);
391:                 g.DrawString(el.Text, new Font("Segoe UI", 9), new SolidBrush(fg), imgLabelRect, sf);
392:                 break;
393:             default:
394:                 g.DrawRectangle(Pens.LightGray, rect);
395:                 g.DrawString(el.Text, font, new SolidBrush(fg), rect, sf);
396:                 break;
397:         }
398:     }
399: 
400:     private GraphicsPath RoundedRect(Rectangle rect, int radius)
401:     {
402:         var path = new GraphicsPath();
403:         int d = radius * 2;
404:         path.AddArc(rect.X, rect.Y, d, d, 180, 90);
405:         path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
406:         path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
407:         path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
408:         path.CloseFigure();
409:         return path;
410:     }
411: }
```

--- Controls/NodeTreePanel.cs ---
```
  1: using System.Diagnostics;
  2: using HtmlDesigner.Models;
  3: 
  4: namespace HtmlDesigner.Controls;
  5: 
  6: public class NodeTreePanel : Panel
  7: {
  8:     private TreeView _treeView;
  9:     private DesignDocument? _document;
 10: 
 11:     public event Action<DesignHtmlElement?>? ElementSelected;
 12:     public event Action? DeleteRequested;
 13: 
 14:     public NodeTreePanel()
 15:     {
 16:         Width = 200;
 17:         BackColor = Color.FromArgb(240, 240, 240);
 18:         Padding = new Padding(0, 28, 0, 0);
 19: 
 20:         var label = new Label
 21:         {
 22:             Text = "Node Tree",
 23:             Location = new Point(0, 0),
 24:             Width = 200,
 25:             Height = 28,
 26:             TextAlign = ContentAlignment.MiddleCenter,
 27:             Font = new Font("Segoe UI", 10, FontStyle.Bold),
 28:             BackColor = Color.FromArgb(220, 220, 220)
 29:         };
 30:         Controls.Add(label);
 31: 
 32:         _treeView = new TreeView
 33:         {
 34:             Dock = DockStyle.Fill,
 35:             BorderStyle = BorderStyle.None,
 36:             BackColor = Color.FromArgb(245, 245, 245),
 37:             Font = new Font("Segoe UI", 10),
 38:             HideSelection = false,
 39:             FullRowSelect = true,
 40:             ShowLines = true,
 41:             ShowPlusMinus = true,
 42:             Indent = 16
 43:         };
 44:         _treeView.AfterSelect += (s, e) =>
 45:         {
 46:             if (e.Node?.Tag is DesignHtmlElement el)
 47:                 ElementSelected?.Invoke(el);
 48:         };
 49:         _treeView.KeyDown += (s, e) =>
 50:         {
 51:             if (e.KeyCode == Keys.Delete)
 52:                 DeleteRequested?.Invoke();
 53:         };
 54:         _treeView.NodeMouseClick += (s, e) =>
 55:         {
 56:             if (e.Button == MouseButtons.Right && e.Node?.Tag is DesignHtmlElement el)
 57:             {
 58:                 _treeView.SelectedNode = e.Node;
 59:                 ElementSelected?.Invoke(el);
 60:                 var ctxMenu = new ContextMenuStrip();
 61:                 var deleteItem = new ToolStripMenuItem("Delete", null, (_, _) => DeleteRequested?.Invoke());
 62:                 ctxMenu.Items.Add(deleteItem);
 63:                 ctxMenu.Show(_treeView, e.Location);
 64:             }
 65:         };
 66:         Controls.Add(_treeView);
 67:     }
 68: 
 69:     public void RefreshTree(DesignDocument doc)
 70:     {
 71:         var selectedId = _treeView.SelectedNode?.Tag is DesignHtmlElement sel ? sel.Id : null;
 72:         try
 73:         {
 74:         _document = doc;
 75:         _treeView.BeginUpdate();
 76:         _treeView.Nodes.Clear();
 77:         var rootNode = new TreeNode("📄 Document") { Tag = null };
 78:         _treeView.Nodes.Add(rootNode);
 79: 
 80:         var rootElements = doc.Elements.Where(e => string.IsNullOrEmpty(e.ParentId)).OrderBy(e => e.ZIndex).ToList();
 81:         foreach (var el in rootElements)
 82:         {
 83:             var node = CreateTreeNode(el, doc.Elements);
 84:             rootNode.Nodes.Add(node);
 85:         }
 86:         rootNode.Expand();
 87:         _treeView.EndUpdate();
 88:         // Restore selection after refresh
 89:         if (selectedId != null)
 90:         {
 91:             SelectNodeById(_treeView.Nodes[0], selectedId);
 92:         }
 93:         }
 94:         catch (Exception ex)
 95:         {
 96:             Debug.WriteLine($"[NodeTreePanel.RefreshTree] Error: {ex.Message}");
 97:             File.AppendAllText("error.log", $"{DateTime.Now}: RefreshTree Error: {ex}\n");
 98:         }
 99:     }
100: 
101:     private TreeNode CreateTreeNode(DesignHtmlElement el, List<DesignHtmlElement> allElements)
102:     {
103:         var node = new TreeNode(GetNodeLabel(el)) { Tag = el };
104:         var children = allElements.Where(e => e.ParentId == el.Id).OrderBy(e => e.ZIndex).ToList();
105:         foreach (var child in children)
106:         {
107:             node.Nodes.Add(CreateTreeNode(child, allElements));
108:         }
109:         return node;
110:     }
111: 
112:     public void SelectElement(DesignHtmlElement? el)
113:     {
114:         try
115:         {
116:         if (el == null)
117:         {
118:             _treeView.SelectedNode = null;
119:             return;
120:         }
121:         if (_treeView.Nodes.Count == 0)
122:             return;
123:         SelectNodeById(_treeView.Nodes[0], el.Id);
124:         }
125:         catch (Exception ex)
126:         {
127:             Debug.WriteLine($"[NodeTreePanel.SelectElement] Error: {ex.Message}");
128:             File.AppendAllText("error.log", $"{DateTime.Now}: SelectElement Error: {ex}\n");
129:         }
130:     }
131: 
132:     private bool SelectNodeById(TreeNode parent, string id)
133:     {
134:         foreach (TreeNode node in parent.Nodes)
135:         {
136:             if (node.Tag is DesignHtmlElement el && el.Id == id)
137:             {
138:                 _treeView.SelectedNode = node;
139:                 node.EnsureVisible();
140:                 return true;
141:             }
142:             if (SelectNodeById(node, id))
143:                 return true;
144:         }
145:         return false;
146:     }
147: 
148:     private string GetNodeLabel(DesignHtmlElement el)
149:     {
150:         var icon = el.Type switch
151:         {
152:             "button" => "🔘",
153:             "panel" => "📦",
154:             "label" => "📝",
155:             "dropdown" => "📋",
156:             "checkbox" => "☑",
157:             "multicheck" => "☑☑",
158:             "image" => "🖼",
159:             _ => "📄"
160:         };
161:         var text = el.Text.Length > 15 ? el.Text[..15] + "..." : el.Text;
162:         return $"{icon} [{el.Type}] {text}";
163:     }
164: }
```

--- Controls/PropertyGrid.cs ---
```
  1: using HtmlDesigner.Models;
  2: 
  3: namespace HtmlDesigner.Controls;
  4: 
  5: public class DesignPropertyGrid : Panel
  6: {
  7:     private bool _updating;
  8:     private DesignHtmlElement? _element;
  9:     private TextBox? _idBox, _textBox, _xBox, _yBox, _wBox, _hBox, _bgBox, _fgBox, _fontBox, _imgBox, _zIndexBox;
 10:     private CheckBox? _checkedBox;
 11:     private ListBox? _optionsBox;
 12:     private Button? _addOptionBtn, _removeOptionBtn;
 13:     private List<Control> _fieldControls = new();
 14: 
 15:     public event Action<DesignHtmlElement>? ElementUpdated;
 16: 
 17:     public DesignHtmlElement? SelectedElement
 18:     {
 19:         get => _element;
 20:         set
 21:         {
 22:             _element = value;
 23:             RefreshControls();
 24:         }
 25:     }
 26: 
 27:     public DesignPropertyGrid()
 28:     {
 29:         Width = 260;
 30:         BackColor = Color.FromArgb(245, 245, 245);
 31:         AutoScroll = true;
 32: 
 33:         var label = new Label
 34:         {
 35:             Text = "Properties",
 36:             Dock = DockStyle.Top,
 37:             Height = 30,
 38:             TextAlign = ContentAlignment.MiddleCenter,
 39:             Font = new Font("Segoe UI", 11, FontStyle.Bold)
 40:         };
 41:         Controls.Add(label);
 42:         BuildFields();
 43:     }
 44: 
 45:     private void BuildFields()
 46:     {
 47:         int y = 40;
 48:         _idBox = AddField("ID", y); y += 50;
 49:         _textBox = AddField("Text", y); y += 50;
 50:         _xBox = AddField("X", y); y += 50;
 51:         _yBox = AddField("Y", y); y += 50;
 52:         _wBox = AddField("Width", y); y += 50;
 53:         _hBox = AddField("Height", y); y += 50;
 54:         _bgBox = AddField("Background", y); y += 50;
 55:         _fgBox = AddField("Foreground", y); y += 50;
 56:         _fontBox = AddField("FontSize", y); y += 50;
 57:         _imgBox = AddField("ImagePath", y); y += 50;
 58:         _zIndexBox = AddField("Z-Index", y); y += 50;
 59: 
 60:         _checkedBox = new CheckBox { Text = "Checked", Location = new Point(10, y), Width = 200 };
 61:         _checkedBox.CheckedChanged += OnFieldChanged;
 62:         Controls.Add(_checkedBox);
 63:         y += 30;
 64: 
 65:         var optLabel = new Label { Text = "Options (one per line):", Location = new Point(10, y), Width = 200, Height = 20 };
 66:         Controls.Add(optLabel);
 67:         y += 20;
 68: 
 69:         _optionsBox = new ListBox { Location = new Point(10, y), Width = 220, Height = 80 };
 70:         _optionsBox.SelectedIndexChanged += OnFieldChanged;
 71:         Controls.Add(_optionsBox);
 72:         y += 85;
 73: 
 74:         _addOptionBtn = new Button { Text = "Add Option", Location = new Point(10, y), Width = 100 };
 75:         _addOptionBtn.Click += (s, e) => { _optionsBox.Items.Add("Option"); ApplyChanges(); };
 76:         Controls.Add(_addOptionBtn);
 77: 
 78:         _removeOptionBtn = new Button { Text = "Remove", Location = new Point(115, y), Width = 100 };
 79:         _removeOptionBtn.Click += (s, e) => { if (_optionsBox.SelectedIndex >= 0) { _optionsBox.Items.RemoveAt(_optionsBox.SelectedIndex); ApplyChanges(); } };
 80:         Controls.Add(_removeOptionBtn);
 81:     }
 82: 
 83:     private TextBox AddField(string label, int y)
 84:     {
 85:         var lbl = new Label { Text = label + ":", Location = new Point(10, y), Width = 80, Height = 20 };
 86:         lbl.Tag = "propLabel";
 87:         var tb = new TextBox { Location = new Point(100, y - 2), Width = 140, Height = 24 };
 88:         tb.Tag = "propField";
 89:         tb.TextChanged += OnFieldChanged;
 90:         Controls.Add(lbl);
 91:         Controls.Add(tb);
 92:         _fieldControls.Add(lbl);
 93:         _fieldControls.Add(tb);
 94:         return tb;
 95:     }
 96: 
 97:     private void RefreshControls()
 98:     {
 99:         _updating = true;
100:         SuspendLayout();
101:         if (_element != null)
102:         {
103:             _idBox!.Text = _element.Id;
104:             _textBox!.Text = _element.Text;
105:             _xBox!.Text = _element.X.ToString();
106:             _yBox!.Text = _element.Y.ToString();
107:             _wBox!.Text = _element.Width.ToString();
108:             _hBox!.Text = _element.Height.ToString();
109:             _bgBox!.Text = _element.BackgroundColor;
110:             _fgBox!.Text = _element.ForegroundColor;
111:             _fontBox!.Text = _element.FontSize.ToString();
112:             _imgBox!.Text = _element.ImagePath;
113:             _zIndexBox!.Text = _element.ZIndex.ToString();
114:             _checkedBox!.Checked = _element.Checked;
115:             _optionsBox!.Items.Clear();
116:             foreach (var o in _element.Options)
117:                 _optionsBox.Items.Add(o);
118:         }
119:         else
120:         {
121:             ClearFields();
122:         }
123:         ResumeLayout();
124:         _updating = false;
125:     }
126: 
127:     private void OnFieldChanged(object? sender, EventArgs e)
128:     {
129:         if (_updating) return;
130:         ApplyChanges();
131:     }
132: 
133:     private void ClearFields()
134:     {
135:         foreach (var ctrl in _fieldControls)
136:         {
137:             if (ctrl is TextBox tb) tb.Text = "";
138:         }
139:         _checkedBox!.Checked = false;
140:         _optionsBox!.Items.Clear();
141:     }
142: 
143:     private void ApplyChanges()
144:     {
145:         if (_updating) return;
146:         if (_element == null) return;
147:         try
148:         {
149:             _element.Id = _idBox!.Text;
150:             _element.Text = _textBox!.Text;
151:             if (int.TryParse(_xBox!.Text, out int x)) _element.X = x;
152:             if (int.TryParse(_yBox!.Text, out int y)) _element.Y = y;
153:             if (int.TryParse(_wBox!.Text, out int w)) _element.Width = w;
154:             if (int.TryParse(_hBox!.Text, out int h)) _element.Height = h;
155:             _element.BackgroundColor = _bgBox!.Text;
156:             _element.ForegroundColor = _fgBox!.Text;
157:             if (int.TryParse(_fontBox!.Text, out int fs)) _element.FontSize = fs;
158:             _element.ImagePath = _imgBox!.Text;
159:             if (int.TryParse(_zIndexBox!.Text, out int z)) _element.ZIndex = z;
160:             _element.Checked = _checkedBox!.Checked;
161:             _element.Options = _optionsBox!.Items.Cast<string>().ToList();
162:             ElementUpdated?.Invoke(_element);
163:         }
164:         catch (Exception ex)
165:         {
166:             System.Diagnostics.Debug.WriteLine($"[ApplyChanges] Error: {ex.Message}");
167:             File.AppendAllText("error.log", $"{DateTime.Now}: ApplyChanges Error: {ex}\n");
168:         }
169:     }
170: }
```

--- Controls/ToolboxControl.cs ---
```
 1: using HtmlDesigner.Models;
 2: 
 3: namespace HtmlDesigner.Controls;
 4: 
 5: public class ToolboxControl : Panel
 6: {
 7:     public event Action<string>? ElementSelected;
 8: 
 9:     public ToolboxControl()
10:     {
11:         Width = 170;
12:         BackColor = Color.FromArgb(240, 240, 240);
13: 
14:         var label = new Label
15:         {
16:             Text = "🧰 Toolbox",
17:             Location = new Point(0, 0),
18:             Width = 168,
19:             Height = 30,
20:             TextAlign = ContentAlignment.MiddleCenter,
21:             Font = new Font("Segoe UI", 11, FontStyle.Bold),
22:             BackColor = Color.FromArgb(66, 133, 244),
23:             ForeColor = Color.White
24:         };
25:         Controls.Add(label);
26: 
27:         var elements = new (string name, string type)[]
28:         {
29:             ("🔘 Button", "button"),
30:             ("📦 Panel", "panel"),
31:             ("📝 Label", "label"),
32:             ("📋 Dropdown", "dropdown"),
33:             ("☑ Checkbox", "checkbox"),
34:             ("☑☑ Multi-Check", "multicheck"),
35:             ("🖼 Image", "image")
36:         };
37: 
38:         int y = 38;
39:         foreach (var (name, type) in elements)
40:         {
41:             var btn = new Button
42:             {
43:                 Text = name,
44:                 Tag = type,
45:                 Location = new Point(5, y),
46:                 Width = 150,
47:                 Height = 36,
48:                 FlatStyle = FlatStyle.Flat,
49:                 BackColor = Color.White,
50:                 TextAlign = ContentAlignment.MiddleLeft,
51:                 Padding = new Padding(8, 0, 0, 0),
52:                 Cursor = Cursors.Hand
53:             };
54:             btn.FlatAppearance.BorderColor = Color.FromArgb(200, 200, 200);
55:             btn.Click += (s, e) => ElementSelected?.Invoke(type);
56:             btn.MouseEnter += (s, e) => { if (s is Button b) b.BackColor = Color.FromArgb(230, 240, 255); };
57:             btn.MouseLeave += (s, e) => { if (s is Button b) b.BackColor = Color.White; };
58:             Controls.Add(btn);
59:             y += 44;
60:         }
61:     }
62: }
```

--- Models/DesignDocument.cs ---
```
1: namespace HtmlDesigner.Models;
2: 
3: public class DesignDocument
4: {
5:     public List<DesignHtmlElement> Elements { get; set; } = new();
6:     public int CanvasWidth { get; set; } = 800;
7:     public int CanvasHeight { get; set; } = 600;
8: }
```

--- Models/HtmlElement.cs ---
```
 1: namespace HtmlDesigner.Models;
 2: 
 3: public class DesignHtmlElement
 4: {
 5:     public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
 6:     public string Type { get; set; } = "div";
 7:     public string Text { get; set; } = "";
 8:     public int X { get; set; }
 9:     public int Y { get; set; }
10:     public int Width { get; set; } = 120;
11:     public int Height { get; set; } = 30;
12:     public string BackgroundColor { get; set; } = "#e0e0e0";
13:     public string ForegroundColor { get; set; } = "#000000";
14:     public int FontSize { get; set; } = 14;
15:     public List<string> Options { get; set; } = new();
16:     public bool Checked { get; set; }
17:     public string ImagePath { get; set; } = "";
18:     public int ZIndex { get; set; }
19:     public string? ParentId { get; set; }
20: 
21:     public DesignHtmlElement Clone()
22:     {
23:         return new DesignHtmlElement
24:         {
25:             Id = Id,
26:             Type = Type,
27:             Text = Text,
28:             X = X,
29:             Y = Y,
30:             Width = Width,
31:             Height = Height,
32:             BackgroundColor = BackgroundColor,
33:             ForegroundColor = ForegroundColor,
34:             FontSize = FontSize,
35:             Options = new List<string>(Options),
36:             Checked = Checked,
37:             ImagePath = ImagePath,
38:             ZIndex = ZIndex,
39:             ParentId = ParentId
40:         };
41:     }
42: }
```

--- MainForm.cs ---
```
  1: using System.Diagnostics;
  2: using HtmlDesigner.Controls;
  3: using HtmlDesigner.Models;
  4: using HtmlDesigner.Services;
  5: 
  6: namespace HtmlDesigner;
  7: 
  8: public class MainForm : Form
  9: {
 10:     private DesignCanvas? _canvas;
 11:     private DesignPropertyGrid? _propertyGrid;
 12:     private NodeTreePanel? _nodeTree;
 13:     private DesignDocument _document;
 14: 
 15:     public MainForm()
 16:     {
 17:         try
 18:         {
 19:         Text = "HTML Designer";
 20:         Size = new Size(1400, 800);
 21:         StartPosition = FormStartPosition.CenterScreen;
 22: 
 23:         _document = new DesignDocument();
 24:         BuildNodeTree();
 25:         _nodeTree?.RefreshTree(_document);
 26:         BuildCanvas();
 27:         BuildToolbox();
 28:         BuildMenu();
 29:         BuildPropertyGrid();
 30:         this.Resize += (s, e) => UpdateLayout();
 31:         UpdateLayout();
 32:         }
 33:         catch (Exception ex)
 34:         {
 35:             File.AppendAllText("error.log", $"{DateTime.Now}: MainForm ctor Error: {ex}\n");
 36:             MessageBox.Show($"启动错误: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
 37:         }
 38:     }
 39: 
 40:     private void UpdateLayout()
 41:     {
 42:         if (_nodeTree != null)
 43:         {
 44:             _nodeTree.Top = 30;
 45:             _nodeTree.Width = 200;
 46:             _nodeTree.Height = this.ClientSize.Height - 30;
 47:         }
 48:         if (_canvas != null)
 49:         {
 50:             _canvas.Left = 385;
 51:             _canvas.Top = 30;
 52:             _canvas.Width = Math.Max(100, this.ClientSize.Width - 655);
 53:             _canvas.Height = Math.Max(100, this.ClientSize.Height - 40);
 54:         }
 55:         if (_propertyGrid != null)
 56:         {
 57:             _propertyGrid.Left = Math.Max(385, this.ClientSize.Width - 260);
 58:             _propertyGrid.Top = 30;
 59:             _propertyGrid.Height = Math.Max(100, this.ClientSize.Height - 30);
 60:         }
 61:     }
 62: 
 63:     private void BuildNodeTree()
 64:     {
 65:         _nodeTree = new NodeTreePanel
 66:         {
 67:             Top = 30,
 68:             Height = this.ClientSize.Height - 30,
 69:             Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left
 70:         };
 71:         _nodeTree.ElementSelected += el => {
 72:             if (_canvas != null) _canvas.SelectedElement = el;
 73:         };
 74:         _nodeTree.DeleteRequested += () => {
 75:             _canvas?.RemoveSelectedElement();
 76:         };
 77:         Controls.Add(_nodeTree);
 78:     }
 79: 
 80:     private void BuildMenu()
 81:     {
 82:         var menu = new MenuStrip();
 83: 
 84:         var fileMenu = new ToolStripMenuItem("File");
 85:         var newItem = new ToolStripMenuItem("New", null, (s, e) => NewDocument());
 86:         var openItem = new ToolStripMenuItem("Open JSON...", null, (s, e) => OpenJson());
 87:         var saveItem = new ToolStripMenuItem("Save JSON...", null, (s, e) => SaveJson());
 88:         var exportItem = new ToolStripMenuItem("Export HTML...", null, (s, e) => ExportHtml());
 89:         var exitItem = new ToolStripMenuItem("Exit", null, (s, e) => Close());
 90: 
 91:         fileMenu.DropDownItems.AddRange(new ToolStripItem[] { newItem, openItem, saveItem, exportItem,
 92:             new ToolStripSeparator(), exitItem });
 93: 
 94:         var editMenu = new ToolStripMenuItem("Edit");
 95:         var deleteItem = new ToolStripMenuItem("Delete Element", null, (s, e) => _canvas?.RemoveSelectedElement());
 96:         deleteItem.ShortcutKeys = Keys.Delete;
 97:         editMenu.DropDownItems.Add(deleteItem);
 98: 
 99:         menu.Items.Add(fileMenu);
100:         menu.Items.Add(editMenu);
101:         MainMenuStrip = menu;
102:         Controls.Add(menu);
103:     }
104: 
105:     private void BuildToolbox()
106:     {
107:         var toolbox = new ToolboxControl();
108:         toolbox.Location = new Point(205, 30);
109:         toolbox.Width = 170;
110:         toolbox.Height = this.ClientSize.Height - 30;
111:         toolbox.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left;
112:         toolbox.BorderStyle = BorderStyle.FixedSingle;
113:         toolbox.ElementSelected += type => _canvas?.AddElement(type);
114:         Controls.Add(toolbox);
115:         toolbox.BringToFront();
116:     }
117: 
118:     private void BuildCanvas()
119:     {
120:         _canvas = new DesignCanvas
121:         {
122:             Document = _document,
123:             Left = 385,
124:             Top = 30,
125:             Width = this.ClientSize.Width - 655,
126:             Height = this.ClientSize.Height - 40,
127:             Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right
128:         };
129:         _canvas.ElementSelectedChanged += el => {
130:             _nodeTree?.SelectElement(el);
131:         };
132:         _canvas.ElementsChanged += () => {
133:             _nodeTree?.RefreshTree(_document);
134:         };
135:         Controls.Add(_canvas);
136:     }
137: 
138:     private void BuildPropertyGrid()
139:     {
140:         _propertyGrid = new DesignPropertyGrid
141:         {
142:             Left = this.ClientSize.Width - 260,
143:             Top = 30,
144:             Height = this.ClientSize.Height - 30,
145:             Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Right
146:         };
147:         if (_canvas != null)
148:         {
149:             _canvas.ElementSelectedChanged += el => {
150:                 if (_propertyGrid != null) _propertyGrid.SelectedElement = el;
151:             };
152:         }
153:         _propertyGrid.ElementUpdated += el => {
154:             _canvas?.UpdateSelectedElement(el);
155:             _nodeTree?.RefreshTree(_document);
156:         };
157:         Controls.Add(_propertyGrid);
158:     }
159: 
160:     private void NewDocument()
161:     {
162:         _document = new DesignDocument();
163:         if (_canvas != null)
164:         {
165:             _canvas.Document = _document;
166:         }
167:         _nodeTree?.RefreshTree(_document);
168:     }
169: 
170:     private void OpenJson()
171:     {
172:         using var dlg = new OpenFileDialog { Filter = "JSON Files|*.json" };
173:         if (dlg.ShowDialog() == DialogResult.OK)
174:         {
175:             var json = File.ReadAllText(dlg.FileName);
176:             var doc = JsonService.LoadFromJson(json);
177:             if (doc != null)
178:             {
179:                 _document = doc;
180:                 if (_canvas != null)
181:                 {
182:                     _canvas.Document = _document;
183:                 }
184:                 _nodeTree?.RefreshTree(_document);
185:             }
186:             else
187:             {
188:                 MessageBox.Show("Failed to load JSON file.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
189:             }
190:         }
191:     }
192: 
193:     private void SaveJson()
194:     {
195:         using var dlg = new SaveFileDialog { Filter = "JSON Files|*.json" };
196:         if (dlg.ShowDialog() == DialogResult.OK)
197:         {
198:             var json = JsonService.SaveToJson(_document);
199:             File.WriteAllText(dlg.FileName, json);
200:             MessageBox.Show("Saved successfully.", "Save JSON", MessageBoxButtons.OK, MessageBoxIcon.Information);
201:         }
202:     }
203: 
204:     private void ExportHtml()
205:     {
206:         using var dlg = new SaveFileDialog { Filter = "HTML Files|*.html" };
207:         if (dlg.ShowDialog() == DialogResult.OK)
208:         {
209:             var html = HtmlExportService.ExportToHtml(_document);
210:             File.WriteAllText(dlg.FileName, html);
211:             MessageBox.Show("HTML exported successfully.", "Export HTML", MessageBoxButtons.OK, MessageBoxIcon.Information);
212:         }
213:     }
214: }
```

--- Program.cs ---
```
 1: namespace HtmlDesigner;
 2: 
 3: static class Program
 4: {
 5:     [STAThread]
 6:     static void Main()
 7:     {
 8:         ApplicationConfiguration.Initialize();
 9:         Application.Run(new MainForm());
10:     }
11: }
```

【目录结构】（最多展示 200 个文件，已过滤常见构建/依赖目录）
HtmlDesigner.csproj
MainForm.cs
MainForm.cs.bak
Program.cs
Controls/DesignCanvas.cs
Controls/DesignCanvas.cs.bak
Controls/NodeTreePanel.cs
Controls/NodeTreePanel.cs.bak
Controls/PropertyGrid.cs
Controls/PropertyGrid.cs.bak
Controls/ToolboxControl.cs
Models/DesignDocument.cs
Models/HtmlElement.cs
Models/HtmlElement.cs.bak
Services/HtmlExportService.cs
Services/JsonService.cs

（未勾选"包含全部文件"，如需其它文件请告诉我）
