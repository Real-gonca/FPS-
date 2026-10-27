#!/usr/bin/env python3
"""Validate the WPF markup without a .NET SDK.

Checks, in order:
  1. every .xaml file is well-formed XML (catches the classic "XML comments
     cannot contain --" mistake and unclosed tags),
  2. every {StaticResource key} used by a view is actually defined,
  3. every {Binding path} resolves to a member of the view model / model,
  4. every Commands.<name> referenced by the view exists.

It is a smoke test, not a XAML compiler: converters, templates and attached
properties are only partially understood. It still catches the two failure
modes that matter most in practice (markup that will not parse, and bindings
that silently do nothing).

Usage:  python3 tests/tools/check_xaml.py [repo_root]
"""

import os
import re
import sys
import xml.etree.ElementTree as ET

# Types declared with array syntax (CurveMode[]) are picked up by this pattern.
PROPERTY = re.compile(r'public\s+(?:[\w<>\(\)\[\],\.\s]+?)\s+(\w+)\s*(?:\{|=>)')


def walk(root, suffix):
    for dirpath, dirs, names in os.walk(root):
        dirs[:] = [d for d in dirs if d not in ('.git', 'bin', 'obj', 'build')]
        for name in names:
            if name.endswith(suffix):
                yield os.path.join(dirpath, name)


def main():
    root = os.path.abspath(sys.argv[1] if len(sys.argv) > 1 else
                           os.path.join(os.path.dirname(__file__), '..', '..'))
    failures = []

    xaml_files = list(walk(root, '.xaml'))
    for path in xaml_files:
        try:
            ET.parse(path)
        except ET.ParseError as error:
            failures.append(f"{os.path.relpath(path, root)}: {error}")

    views = os.path.join(root, 'app', 'MainWindow.xaml')
    theme = os.path.join(root, 'app', 'Resources', 'Theme.xaml')

    if not (os.path.exists(views) and os.path.exists(theme)):
        print('app markup not found; nothing to check')
        return 0 if not failures else 1

    view = open(views, encoding='utf-8').read()
    theme_text = open(theme, encoding='utf-8').read()

    defined = set(re.findall(r'x:Key="([^"]+)"', theme_text)) | \
              set(re.findall(r'x:Key="([^"]+)"', view))

    for key in re.findall(r'\{StaticResource ([^}]+)\}', view):
        if key.strip() not in defined:
            failures.append(f"missing StaticResource '{key.strip()}'")

    vm = open(os.path.join(root, 'app', 'ViewModels', 'MainViewModel.cs'),
              encoding='utf-8').read()
    commands = open(os.path.join(root, 'app', 'ViewModels', 'Commands.cs'),
                    encoding='utf-8').read()
    stage = set(re.findall(r'public\s+\w+\s+(ScaleX|ScaleY)', vm))

    members = set(PROPERTY.findall(vm))

    for match in re.finditer(r'\{Binding ([^},]+)', view):
        path = match.group(1).strip()
        if '=' in path:          # ElementName=..., RelativeSource=...
            continue
        head, _, tail = path.partition('.')
        if head == 'SelectedStage':
            if tail and tail not in stage:
                failures.append(f"binding '{path}': StageItem has no '{tail}'")
        elif head not in members:
            failures.append(f"binding '{path}': MainViewModel has no '{head}'")

    known = set(re.findall(r'public\s+ICommand\s+(\w+)', commands))
    for name in set(re.findall(r'Commands\.(\w+)', view)):
        if name not in known:
            failures.append(f"Commands.{name} does not exist")

    if failures:
        print(f"{len(failures)} markup problem(s):")
        for item in failures:
            print("  -", item)
        return 1

    print(f"OK: {len(xaml_files)} XAML file(s) well-formed, "
          f"bindings and resources resolve")
    return 0


if __name__ == '__main__':
    sys.exit(main())
