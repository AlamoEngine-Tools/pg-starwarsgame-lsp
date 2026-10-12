import typescriptEslint from "@typescript-eslint/eslint-plugin";
import tsParser from "@typescript-eslint/parser";
import stylistic from "@stylistic/eslint-plugin";

export default [{
    files: ["**/*.ts", "**/*.tsx"],
}, {
    plugins: {
        "@typescript-eslint": typescriptEslint,
        "@stylistic": stylistic,
    },

    languageOptions: {
        parser: tsParser,
        ecmaVersion: 2022,
        sourceType: "module",
    },

    rules: {
        "@typescript-eslint/naming-convention": ["warn", {
            selector: "import",
            format: ["camelCase", "PascalCase"],
        }],

        curly: "warn",
        eqeqeq: "warn",
        "no-throw-literal": "warn",
        semi: "warn",

        // The format check. Rider is the formatter (4-space indent from .editorconfig); these are
        // the rules its output satisfies in every file, measured over the whole client. Indent is
        // not among them: Rider aligns continuations and JSX in ways the rule cannot express, and
        // enforcing it would fail code Rider just formatted. Neither is brace spacing, which
        // Rider sets per construct (none in object literals, one in type literals).
        "@stylistic/quotes": ["error", "single", {avoidEscape: true, allowTemplateLiterals: "always"}],
        "@stylistic/semi": ["error", "always"],
        "@stylistic/comma-spacing": "error",
        "@stylistic/keyword-spacing": "error",
        "@stylistic/space-infix-ops": "error",
        "@stylistic/space-before-blocks": "error",
        "@stylistic/no-trailing-spaces": "error",
        "@stylistic/brace-style": ["error", "1tbs", {allowSingleLine: true}],
        "@stylistic/key-spacing": "error",
        "@stylistic/arrow-spacing": "error",
        "@stylistic/space-in-parens": ["error", "never"],
        "@stylistic/array-bracket-spacing": ["error", "never"],
        "@stylistic/no-multi-spaces": ["error", {ignoreEOLComments: true}],
        "@stylistic/function-call-spacing": "error",
        "@stylistic/space-before-function-paren": ["error", {anonymous: "never", named: "never", asyncArrow: "always"}],
        "@stylistic/jsx-curly-spacing": ["error", "never"],
        "@stylistic/eol-last": "error",
    },
}];
