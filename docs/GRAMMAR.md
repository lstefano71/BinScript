# BinScript Formal Grammar

This document defines the formal grammar of the BinScript (`.bsx`) language using Extended Backus–Naur Form (EBNF). It is the authoritative reference for the language syntax and is derived directly from the parser and lexer implementation.

For informal documentation with examples, see [LANGUAGE_SPEC.md](LANGUAGE_SPEC.md).

## Notation

| Symbol | Meaning |
|--------|---------|
| `=` | Definition |
| `;` | End of production |
| `\|` | Alternative |
| `{ x }` | Zero or more repetitions of *x* |
| `[ x ]` | Optional *x* (zero or one) |
| `( x )` | Grouping |
| `'...'` | Terminal (literal token) |
| `UPPER_CASE` | Terminal token class (from lexer) |
| `PascalCase` | Non-terminal (grammar rule) |

---

## 1. Lexical Grammar

### 1.1 Whitespace and Comments

```ebnf
Whitespace     = ' ' | '\t' | '\r' | '\n' ;
LineComment    = '//' { AnyCharExceptNewline } ;
BlockComment   = '/*' { AnyChar } '*/' ;
```

Whitespace and comments are skipped by the lexer and do not appear in the syntactic grammar.

### 1.2 Identifiers

```ebnf
IDENTIFIER     = IdentStart { IdentContinue } ;
IdentStart     = 'a'..'z' | 'A'..'Z' | '_' ;
IdentContinue  = 'a'..'z' | 'A'..'Z' | '0'..'9' | '_' ;
```

A bare `_` (single underscore) is lexed as the special `UNDERSCORE` token, not as an identifier. All other identifiers are checked against the keyword table (§1.5).

### 1.3 Numeric Literals

```ebnf
INT_LITERAL    = DecLiteral | HexLiteral | BinLiteral | OctLiteral ;
DecLiteral     = Digit { Digit } ;
HexLiteral     = '0' ( 'x' | 'X' ) HexDigit { HexDigit } ;
BinLiteral     = '0' ( 'b' | 'B' ) BinDigit { BinDigit } ;
OctLiteral     = '0' ( 'o' | 'O' ) OctDigit { OctDigit } ;

Digit          = '0'..'9' ;
HexDigit       = '0'..'9' | 'a'..'f' | 'A'..'F' ;
BinDigit       = '0' | '1' ;
OctDigit       = '0'..'7' ;
```

All numeric literals are parsed as 64-bit integers.

### 1.4 String Literals

```ebnf
STRING_LITERAL = '"' { StringChar } '"' ;
StringChar     = EscapeSeq | AnyCharExceptQuoteNewlineBackslash ;
EscapeSeq      = '\\' ( '\\' | '"' | 'n' | 'r' | 't' | '0' | HexEscape ) ;
HexEscape      = 'x' HexDigit HexDigit ;
```

### 1.5 Keywords

| Keyword | Token | Keyword | Token |
|---------|-------|---------|-------|
| `struct` | Struct | `match` | Match |
| `bits` | Bits | `when` | When |
| `enum` | Enum | `if` | If |
| `const` | Const | `else` | Else |
| `bit` | Bit | `bool` | Bool |
| `bytes` | Bytes | `ptr` | Ptr |
| `cstring` | CString | `relptr` | RelPtr |
| `string` | String | `null` | NullLiteral |
| `fixed_string` | FixedString | `true` | TrueLiteral |
| | | `false` | FalseLiteral |

### 1.6 Primitive Type Keywords

**Endian-unqualified** (use the file's `@default_endian`):

| Token | | Token | | Token |
|-------|--|-------|--|-------|
| `u8` | | `i8` | | |
| `u16` | | `i16` | | `f32` |
| `u32` | | `i32` | | `f64` |
| `u64` | | `i64` | | |

**Endian-qualified**:

| Little-endian | Big-endian | | Little-endian | Big-endian |
|---------------|------------|--|---------------|------------|
| `u16le` | `u16be` | | `i16le` | `i16be` |
| `u32le` | `u32be` | | `i32le` | `i32be` |
| `u64le` | `u64be` | | `i64le` | `i64be` |
| `f32le` | `f32be` | | `f64le` | `f64be` |

### 1.7 Directives

Directives are `@`-prefixed keywords recognized by the lexer as distinct tokens.

| Directive | Token | Directive | Token |
|-----------|-------|-----------|-------|
| `@root` | Root | `@until` | Until |
| `@default_endian` | DefaultEndian | `@until_sentinel` | UntilSentinel |
| `@default_encoding` | DefaultEncoding | `@greedy` | Greedy |
| `@param` | Param | `@input_size` | InputSize |
| `@import` | Import | `@offset` | Offset |
| `@coverage` | Coverage | `@remaining` | Remaining |
| `@let` | Let | `@sizeof` | SizeOf |
| `@seek` | Seek | `@offsetof` | OffsetOf |
| `@at` | At | `@count` | Count |
| `@align` | Align | `@strlen` | StrLen |
| `@skip` | Skip | `@crc32` | Crc32 |
| `@hidden` | Hidden | `@adler32` | Adler32 |
| `@derived` | Derived | `@map` | Map |
| `@assert` | Assert | `@max_depth` | MaxDepth |
| `@encoding` | Encoding | `@inline` | Inline |
| | | `@show_ptr` | ShowPtr |

### 1.8 Operators and Punctuation

**Two-character operators** (matched before single-character):

| Token | Lexeme | Token | Lexeme |
|-------|--------|-------|--------|
| `=>` | Arrow | `>=` | GreaterEqual |
| `==` | EqualEqual | `>>` | RightShift |
| `!=` | BangEqual | `&&` | AmpersandAmpersand |
| `<=` | LessEqual | `\|\|` | PipePipe |
| `<<` | LeftShift | `..` | DotDot |

**Single-character tokens**:

| Token | Lexeme | Token | Lexeme | Token | Lexeme |
|-------|--------|-------|--------|-------|--------|
| `{` | LeftBrace | `.` | Dot | `&` | Ampersand |
| `}` | RightBrace | `+` | Plus | `\|` | Pipe |
| `(` | LeftParen | `-` | Minus | `^` | Caret |
| `)` | RightParen | `*` | Star | `~` | Tilde |
| `[` | LeftBracket | `/` | Slash | `!` | Bang |
| `]` | RightBracket | `%` | Percent | `?` | QuestionMark |
| `,` | Comma | `=` | Equal | `<` | Less |
| `:` | Colon | | | `>` | Greater |
| `;` | Semicolon | | | | |

---

## 2. Syntactic Grammar

### 2.1 Script File

```ebnf
ScriptFile     = { TopLevelDecl } EOF ;

TopLevelDecl   = DefaultEndian
               | DefaultEncoding
               | ImportDecl
               | ParamDecl
               | ConstDecl
               | MapDecl
               | EnumDecl
               | BitsStructDecl
               | [ '@root' ] [ CoverageAnnot ] StructDecl
               ;
```

### 2.2 File-Level Directives

```ebnf
DefaultEndian  = '@default_endian' '(' ( 'little' | 'big' ) ')' ;

DefaultEncoding
               = '@default_encoding' '(' IDENTIFIER ')' ;

ImportDecl     = '@import' STRING_LITERAL ;

ParamDecl      = '@param' IDENTIFIER ':' TypeReference ;

CoverageAnnot  = '@coverage' '(' 'partial' ')' ;
```

### 2.3 Struct Declaration

```ebnf
StructDecl     = 'struct' IDENTIFIER [ ParamList ] [ MaxDepth ] '{' Members '}' ;

ParamList      = '(' IDENTIFIER { ',' IDENTIFIER } ')' ;

MaxDepth       = '@max_depth' '(' INT_LITERAL ')' ;

Members        = { Member [ ',' ] } ;
```

Trailing commas after members are optional.

### 2.4 Struct Members

```ebnf
Member         = SeekDirective
               | AtBlock
               | LetBinding
               | AlignDirective
               | SkipDirective
               | AssertDirective
               | DerivedField
               | HiddenField
               | FieldDecl
               ;
```

### 2.5 Directives

```ebnf
SeekDirective  = '@seek' '(' Expression ')' ;

AtBlock        = '@at' '(' Expression ')' [ 'when' Expression ] '{' Members '}' ;

LetBinding     = '@let' IDENTIFIER '=' Expression ;

AlignDirective = '@align' '(' Expression ')' ;

SkipDirective  = '@skip' '(' INT_LITERAL ')' ;

AssertDirective
               = '@assert' '(' Expression ',' STRING_LITERAL ')' ;
```

Note: `@skip` takes only a literal integer, not a general expression.

### 2.6 Field Declarations

```ebnf
FieldDecl      = FieldName ':' TypeReference [ '=' Expression ] [ ArraySpec ]
                 { FieldModifier } ;

DerivedField   = '@derived' FieldName ':' TypeReference '=' Expression ;

HiddenField    = '@hidden' FieldDecl ;

FieldName      = IDENTIFIER | '_' ;
```

### 2.7 Field Modifiers

```ebnf
FieldModifier  = '@hidden'
               | '@encoding' '(' IDENTIFIER ')'
               | '@assert' '(' Expression ',' STRING_LITERAL ')'
               | '@inline'
               | '@show_ptr'
               ;
```

Field modifiers may appear in any order after the array spec.

### 2.8 Array Specifications

```ebnf
ArraySpec      = CountArray | UntilArray | SentinelArray | GreedyArray ;

CountArray     = '[' Expression ']' ;

UntilArray     = '[' ']' '@until' '(' Expression ')' ;

SentinelArray  = '[' ']' '@until_sentinel' '(' IDENTIFIER '=>' Expression ')' ;

GreedyArray    = '[' ']' '@greedy' ;
```

### 2.9 Type References

```ebnf
TypeReference  = BaseTypeRef [ '?' ] ;

BaseTypeRef    = PrimitiveType
               | 'cstring'
               | 'string' '[' Expression ']'
               | 'fixed_string' '[' Expression ']'
               | 'bytes' '[' Expression ']'
               | 'bit'
               | 'bits' '[' INT_LITERAL ']'
               | PtrType
               | MatchExpr
               | NamedType
               ;

PrimitiveType  = 'u8' | 'u16' | 'u32' | 'u64'
               | 'i8' | 'i16' | 'i32' | 'i64'
               | 'f32' | 'f64'
               | 'u16le' | 'u16be' | 'u32le' | 'u32be' | 'u64le' | 'u64be'
               | 'i16le' | 'i16be' | 'i32le' | 'i32be' | 'i64le' | 'i64be'
               | 'f32le' | 'f32be' | 'f64le' | 'f64be'
               | 'bool'
               ;

PtrType        = ( 'ptr' | 'relptr' ) '<' BaseTypeRef { PtrModifier }
                 [ ',' PrimitiveType ] '>' ;

PtrModifier    = '@encoding' '(' IDENTIFIER ')'
               | '@hidden'
               ;

NamedType      = IDENTIFIER [ '(' ArgList ')' ] ;

ArgList        = Expression { ',' Expression } ;
```

The `?` suffix makes any type nullable. Pointer types may specify inner modifiers and an optional width type (defaults to `u32`).

### 2.10 Match Expression

```ebnf
MatchExpr      = 'match' '(' Expression ')' '{' { MatchArm [ ',' ] } '}' ;

MatchArm       = MatchPattern [ 'when' Expression ] '=>' TypeReference [ ArraySpec ] ;

MatchPattern   = WildcardPattern
               | RangePattern
               | IdentifierPattern
               | ValuePattern
               ;

WildcardPattern    = '_' ;
RangePattern       = Expression '..' Expression ;
IdentifierPattern  = IDENTIFIER ;
ValuePattern       = Expression ;
```

**Pattern disambiguation**: The parser first attempts to parse an expression. Then:
1. If it was `_` → `WildcardPattern`
2. If followed by `..` → consume, parse high bound → `RangePattern`
3. If it was a bare identifier AND followed by `when` → `IdentifierPattern`
4. Otherwise → `ValuePattern`

An `IdentifierPattern` binds the discriminant value to the given name for use in the guard expression (e.g., `s when s.starts_with(".debug")`).

### 2.11 Enum Declaration

```ebnf
EnumDecl       = 'enum' IDENTIFIER ':' TypeReference '{' { EnumVariant [ ',' ] } '}' ;

EnumVariant    = IDENTIFIER '=' Expression ;
```

### 2.12 Bits Struct Declaration

```ebnf
BitsStructDecl = 'bits' 'struct' IDENTIFIER ':' TypeReference
                 '{' { BitFieldDecl [ ',' ] } '}' ;

BitFieldDecl   = FieldName ':' BitFieldType ;

BitFieldType   = 'bit' | 'bits' '[' INT_LITERAL ']' ;
```

### 2.13 Const Declaration

```ebnf
ConstDecl      = 'const' IDENTIFIER '=' Expression ';' ;
```

Note: `const` uses a semicolon terminator (unlike struct members which use commas).

### 2.14 Map Declaration

```ebnf
MapDecl        = '@map' IDENTIFIER '(' [ MapParamList ] ')' ':' AnnotTypeRef '=' Expression ;

MapParamList   = MapParam { ',' MapParam } ;

MapParam       = IDENTIFIER ':' AnnotTypeRef ;

AnnotTypeRef   = TypeReference [ '[' ']' ] ;
```

Maps are pure inlined expressions (see [ADR-001](adr/ADR-001-map-inlining.md)). The annotation type reference allows an optional bare array suffix.

---

## 3. Expression Grammar

### 3.1 Expressions

```ebnf
Expression     = UnaryExpr { BinaryOp UnaryExpr } ;
```

Binary operators are parsed using precedence climbing (Pratt parsing). All binary operators are left-associative.

### 3.2 Precedence Table

| Prec | Operators | Description |
|------|-----------|-------------|
| 1 | `\|\|` | Logical OR |
| 2 | `&&` | Logical AND |
| 3 | `\|` | Bitwise OR |
| 4 | `^` | Bitwise XOR |
| 5 | `&` | Bitwise AND |
| 6 | `==` `!=` | Equality |
| 7 | `<` `>` `<=` `>=` | Comparison |
| 8 | `<<` `>>` | Bit shift |
| 9 | `+` `-` | Additive |
| 10 | `*` `/` `%` | Multiplicative |

Precedence 1 is the loosest (evaluated last); precedence 10 is the tightest.

### 3.3 Unary Expressions

```ebnf
UnaryExpr      = PrefixOp UnaryExpr
               | PostfixExpr ;

PrefixOp       = '!'                              (* logical NOT *)
               | '~'                              (* bitwise NOT *)
               | '-'                              (* negation *)
               ;
```

Prefix operators bind tighter than all binary operators.

### 3.4 Postfix Expressions

```ebnf
PostfixExpr    = PrimaryExpr { PostfixOp } ;

PostfixOp      = '.' IDENTIFIER '(' [ ArgList ] ')'   (* method call *)
               | '.' IDENTIFIER                        (* field access *)
               | '[' Expression ']'                    (* index access *)
               ;
```

### 3.5 Primary Expressions

```ebnf
PrimaryExpr    = INT_LITERAL
               | STRING_LITERAL
               | 'true'
               | 'false'
               | 'null'
               | LambdaExpr
               | FunctionCall
               | IDENTIFIER
               | '(' Expression ')'
               | BlockExpr
               | BuiltinZeroArg
               | BuiltinCall
               ;

LambdaExpr     = IDENTIFIER '=>' Expression ;

FunctionCall   = IDENTIFIER '(' [ ArgList ] ')' ;

BlockExpr      = '{' { '@let' IDENTIFIER '=' Expression ',' } Expression '}' ;

BuiltinZeroArg = '@remaining' | '@offset' | '@input_size' ;

BuiltinCall    = ( '@sizeof' | '@offsetof' | '@count' | '@strlen'
               | '@crc32' | '@adler32' ) '(' ArgList ')' ;
```

**Disambiguation**: When an `IDENTIFIER` is encountered:
1. If followed by `=>` → `LambdaExpr`
2. If followed by `(` → `FunctionCall`
3. Otherwise → identifier reference

### 3.6 Built-in Methods

Method calls on values are syntactically generic (`expr.name(args)`). The following methods are recognized by the compiler:

| Method | Signature | Description |
|--------|-----------|-------------|
| `.starts_with(s)` | `string → bool` | Tests if string starts with prefix |
| `.ends_with(s)` | `string → bool` | Tests if string ends with suffix |
| `.contains(s)` | `string → bool` | Tests if string contains substring |
| `.find(pred)` | `array → element` | First element matching predicate |
| `.find_or(pred, default)` | `array → element` | First match or default |
| `.any(pred)` | `array → bool` | Any element matches predicate |
| `.all(pred)` | `array → bool` | All elements match predicate |

Array search methods (`.find`, `.find_or`, `.any`, `.all`) take a lambda expression as the predicate argument.

---

## 4. Grammar Summary

| Category | Count |
|----------|-------|
| File-level declarations | 9 |
| Struct member types | 9 |
| Type reference forms | 10 |
| Array specification forms | 4 |
| Match pattern types | 4 |
| Expression precedence levels | 10 |
| Prefix operators | 3 |
| Binary operators | 17 |
| Postfix operations | 3 |
| Directive tokens | 31 |
| Primitive type tokens | 33 |
| Keywords | 17 |
| Total token types | ~90 |
