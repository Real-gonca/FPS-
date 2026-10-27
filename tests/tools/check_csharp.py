#!/usr/bin/env python3
"""Parse every C# file with tree-sitter to catch syntax errors.

Optional: needs two packages, and is not part of `make test` because the
default build machine may not have them.

    pip install tree_sitter tree_sitter_c_sharp
    python3 tests/tools/check_csharp.py

This validates *syntax only*. Type checking still needs `dotnet build`.
"""

import os
import sys

try:
    from tree_sitter import Language, Parser
    import tree_sitter_c_sharp
except ImportError:
    print("skipped: pip install tree_sitter tree_sitter_c_sharp")
    sys.exit(0)


def main():
    root = os.path.abspath(sys.argv[1] if len(sys.argv) > 1 else
                           os.path.join(os.path.dirname(__file__), '..', '..'))

    parser = Parser(Language(tree_sitter_c_sharp.language()))
    failures = 0
    count = 0

    for dirpath, dirs, names in os.walk(root):
        dirs[:] = [d for d in dirs if d not in ('.git', 'bin', 'obj', 'build')]
        for name in sorted(names):
            if not name.endswith('.cs'):
                continue

            path = os.path.join(dirpath, name)
            source = open(path, 'rb').read()
            count += 1

            stack = [parser.parse(source).root_node]
            errors = []
            while stack:
                node = stack.pop()
                if node.type == 'ERROR' or node.is_missing:
                    errors.append(node)
                stack.extend(node.children)

            for node in errors[:5]:
                line = source[:node.start_byte].count(b'\n') + 1
                snippet = source[node.start_byte:node.end_byte][:100].decode('utf-8', 'replace')
                print(f"{os.path.relpath(path, root)}:{line}: {node.type} {snippet!r}")
            if errors:
                failures += 1

    if failures:
        print(f"{failures} of {count} C# file(s) have syntax errors")
        return 1

    print(f"OK: {count} C# file(s) parse cleanly")
    return 0


if __name__ == '__main__':
    sys.exit(main())
