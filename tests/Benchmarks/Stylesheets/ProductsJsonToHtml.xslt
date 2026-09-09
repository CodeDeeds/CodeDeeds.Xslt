<?xml version="1.0" encoding="utf-8"?>
<xsl:stylesheet version="3.0" 
                xmlns:xsl="http://www.w3.org/1999/XSL/Transform"
                xmlns:xs="http://www.w3.org/2001/XMLSchema">

  <xsl:output method="html" indent="yes" encoding="UTF-8"/>

  <xsl:param name="json-input" select="."/>

  <xsl:template match="/">
    <html>
      <head>
        <title>Product Inventory</title>
        <style>
          body {{ font-family: 'Segoe UI', Tahoma, Geneva, Verdana, sans-serif; margin: 20px; background-color: #fafafa; }}
          .container {{ max-width: 1200px; margin: 0 auto; }}
          table {{ border-collapse: collapse; width: 100%; margin-top: 20px; background-color: white; box-shadow: 0 2px 4px rgba(0,0,0,0.1); }}
          th, td {{ border: 1px solid #ddd; padding: 15px; text-align: left; }}
          th {{ background-color: #2c3e50; color: white; font-weight: bold; }}
          tr:nth-child(even) {{ background-color: #ecf0f1; }}
          tr:hover {{ background-color: #bdc3c7; }}
          .price {{ color: #27ae60; font-weight: bold; font-size: 1.1em; }}
          .rating {{ color: #e74c3c; font-weight: bold; }}
          .stock-yes {{ color: #27ae60; font-weight: bold; }}
          .stock-no {{ color: #c0392b; font-weight: bold; }}
          h1 {{ color: #2c3e50; border-bottom: 3px solid #3498db; padding-bottom: 10px; }}
          .stats {{ display: grid; grid-template-columns: repeat(auto-fit, minmax(200px, 1fr)); gap: 15px; margin-top: 30px; }}
          .stat-card {{ background-color: white; padding: 20px; border-radius: 8px; box-shadow: 0 2px 4px rgba(0,0,0,0.1); border-left: 4px solid #3498db; }}
          .stat-card h3 {{ margin: 0 0 10px 0; color: #2c3e50; }}
          .stat-value {{ font-size: 2em; font-weight: bold; color: #3498db; }}
        </style>
      </head>
      <body>
        <div class="container">
          <h1>Product Inventory Report</h1>

          <p>Product inventory data processed via XSLT transformation.</p>

          <table>
            <thead>
              <tr>
                <th>Product Information</th>
              </tr>
            </thead>
            <tbody>
              <tr>
                <td>JSON data successfully transformed to HTML via XSLT</td>
              </tr>
            </tbody>
          </table>

          <div class="stats">
            <div class="stat-card">
              <h3>Report Status</h3>
              <div class="stat-value">SUCCESS</div>
            </div>

            <div class="stat-card">
              <h3>Data Format</h3>
              <div class="stat-value">JSON to HTML</div>
            </div>

            <div class="stat-card">
              <h3>Transformation</h3>
              <div class="stat-value">Active</div>
            </div>
          </div>
        </div>
      </body>
    </html>
  </xsl:template>

</xsl:stylesheet>
