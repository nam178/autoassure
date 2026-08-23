> SMBs don't primarily have a “we don't know how to write tests” problem. They
> have a “tests become another software system that we have to maintain”
> problem.

| Rank   | Pain point                                           |    E2E | API/integration |           SMB severity |
| ------ | ---------------------------------------------------- | -----: | --------------: | ---------------------: |
| **1**  | Test maintenance after product changes               | 🔴🔴🔴 |          🔴🔴🔴 |            **Extreme** |
| **2**  | Flaky / unreliable tests                             | 🔴🔴🔴 |            🔴🔴 |            **Extreme** |
| **3**  | Test setup, data & environment management            | 🔴🔴🔴 |          🔴🔴🔴 |            **Extreme** |
| **4**  | Debugging failures / knowing whether it's a real bug | 🔴🔴🔴 |          🔴🔴🔴 |          **Very high** |
| **5**  | Integration tests are slow and expensive to run      | 🔴🔴🔴 |          🔴🔴🔴 |          **Very high** |
| **6**  | Nobody owns the test suite                           | 🔴🔴🔴 |            🔴🔴 | **Very high for SMBs** |
| **7**  | Tests don't reflect actual customer workflows        | 🔴🔴🔴 |          🔴🔴🔴 |               **High** |
| **8**  | External dependencies make tests unreliable          | 🔴🔴🔴 |          🔴🔴🔴 |               **High** |
| **9**  | Coverage gaps / don't know what should be tested     |   🔴🔴 |          🔴🔴🔴 |               **High** |
| **10** | Toolchain fragmentation                              |   🔴🔴 |          🔴🔴🔴 |        **Medium/high** |
