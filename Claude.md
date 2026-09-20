# Claude Project Context

## Project Overview
This project is an open source project that is a light weight XSLT transformation library written in .NET, with no dependencies to other than standard .NET system libraries. The goal is to provide a fast and efficient XSLT transformation solution for .NET applications.

## Technology Stack
- The project is built using .NET and C#. It leverages the standard .NET system libraries to ensure compatibility and performance.

## Directory Structure
├── CodeDeeds.Xslt/# Main library source code   
│ ├── Compiler/ # XSLT compiler  
│ ├── Documentation/ # Documentation for the library  
│ ├── Emit/ # Classes for IL code generation  
│ ├── Model/ # Document model  
│ ├── Runtime/ # API endpoints  
│ └── XPath/ # XPath implementation  
├── Benchmarks/# Benchmarking and performance tests 
├── UnitTests/ # Unit tests for the library  
└── W3CConformanceTests # W3C conformance tests for XSLT and XPath  

## Development Guidelines
- Performance is a key feature for the project, so always work hard for making the code be as efficient and fast as possible.
- Add unit tests for any new feature or bug fix to ensure code quality and maintainability.
- Include documentation comments on public methods and classes to provide intelisence and clear explanations of their functionality. Provide example code snippets where applicable.

## Documentation
- XsltCompatibility.md contains specifications of which areas of XSLT and XPath is covered and which parts that is not implemented or partly implemented. Keep it compact and easy to understand to quickly get an overview of what is implemented. This document must always be kept up to date after changes.
- ConformanceNotes.md contains notes about the W3C conformance tests and which tests that are passed and which tests that are not passed. This document is mostly for your reference and should be kept up to date after changes.

## External Resources
- [XSLT Documentation](https://www.w3.org/TR/xslt-30/)
- [XSL Style Sheets](https://www.w3.org/Style/XSL/)
- [GitHub Repository of XSLT tests](https://github.com/w3c/xslt30-test/tree/master)
- [GitHub Repository of XQuery tests](https://github.com/w3c/qtspecs/tree/master)

---
**Last Updated**: 2026-09-19  
**Maintained by**: Henning Nybro Johnsrud