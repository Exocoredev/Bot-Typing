namespace AutoInputTester.Dom;

public static class TypewriterDomScript
{
    /// <summary>
    /// Reads remaining exercise text from Typewriter EDU (edu.typewriter.at)
    /// and legacy typewriter.at pages (#text_todo_1 child spans).
    /// </summary>
    public const string ExtractRemainingText = """
(() => {
  const clean = (s) => (s || '').replace(/\u00A0/g, ' ').replace(/\r/g, '');
  const fromTodo = (el) => {
    if (!el) return '';
    let t = '';
    if (el.children && el.children.length > 0) {
      for (const c of el.children) t += c.innerText || c.textContent || '';
    }
    if (!t) t = el.innerText || el.textContent || '';
    return clean(t);
  };

  // Typewriter EDU / classic: remaining chars live in #text_todo_1
  const todo = document.getElementById('text_todo_1');
  const primary = fromTodo(todo);
  if (primary.length > 0) return primary;

  for (const id of [
    'text_todo', 'text_todo_0', 'text_todo_2',
    'uebungstext', 'exerciseText', 'tw-text', 'letterBox', 'textBox'
  ]) {
    const t = fromTodo(document.getElementById(id));
    if (t.length > 3) return t;
  }

  // EDU sometimes wraps letters in spans with class hints
  for (const sel of [
    '#text_todo_1 span', '[id*="text_todo"]', '.text_todo',
    '[class*="todo"]', '[class*="exercise"]'
  ]) {
    try {
      const root = document.querySelector(sel);
      if (!root) continue;
      const host = root.id && root.id.indexOf('text_todo') === 0 ? root : root.parentElement;
      const t = fromTodo(host || root);
      if (t.length > 3) return t;
    } catch (_) {}
  }

  // Heuristic: container with many single-char children (jzj tft …)
  let best = '';
  const nodes = document.querySelectorAll('div, span, p, td');
  for (const n of nodes) {
    const kids = n.children ? n.children.length : 0;
    if (kids < 20) continue;
    let single = 0;
    for (let i = 0; i < Math.min(kids, 40); i++) {
      const ct = (n.children[i].innerText || n.children[i].textContent || '').replace(/\u00A0/g, ' ');
      if (ct.length <= 1) single++;
    }
    if (single < 15) continue;
    const t = fromTodo(n);
    if (t.length > best.length && t.length < 20000) best = t;
  }
  return best;
})()
""";
}
