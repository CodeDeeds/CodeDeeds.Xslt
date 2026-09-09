<?xml version="1.0" encoding="utf-8"?>
<xsl:stylesheet version="3.0" 
                xmlns:xsl="http://www.w3.org/1999/XSL/Transform"
                xmlns:xs="http://www.w3.org/2001/XMLSchema">

  <xsl:output method="html" indent="yes" encoding="UTF-8"/>

  <xsl:template match="/">
    <html>
      <head>
        <title>Product Catalog</title>
        <style>
          body {{ font-family: Arial, sans-serif; margin: 20px; }}
          table {{ border-collapse: collapse; width: 100%; margin-top: 20px; }}
          th, td {{ border: 1px solid #ddd; padding: 12px; text-align: left; }}
          th {{ background-color: #4CAF50; color: white; }}
          tr:nth-child(even) {{ background-color: #f2f2f2; }}
          tr:hover {{ background-color: #ddd; }}
          .price {{ color: #2196F3; font-weight: bold; }}
          .rating {{ color: #FF9800; }}
          .inStock {{ color: #4CAF50; }}
          .outOfStock {{ color: #f44336; }}
          h1 {{ color: #333; }}
        </style>
      </head>
      <body>
        <h1>Product Catalog</h1>
        <p>Total Products: <xsl:value-of select="count(//product)"/></p>

        <table>
          <thead>
            <tr>
              <th>ID</th>
              <th>Name</th>
              <th>Category</th>
              <th>Price (USD)</th>
              <th>Description</th>
              <th>In Stock</th>
              <th>Rating</th>
            </tr>
          </thead>
          <tbody>
            <xsl:apply-templates select="//product" mode="row"/>
          </tbody>
        </table>

        <div style="margin-top: 30px; padding: 15px; background-color: #f5f5f5; border-radius: 5px;">
          <h2>Summary</h2>
          <p>Average Price: $<xsl:value-of select="format-number(avg(//product/price), '0.00')"/></p>
          <p>Average Rating: <xsl:value-of select="format-number(avg(//product/rating), '0.0')"/></p>
          <p>Products in Stock: <xsl:value-of select="count(//product[inStock='true'])"/></p>
          <p>Products out of Stock: <xsl:value-of select="count(//product[inStock='false'])"/></p>
        </div>
      </body>
    </html>
  </xsl:template>

  <xsl:template match="product" mode="row">
    <tr>
      <td><xsl:value-of select="id"/></td>
      <td><xsl:value-of select="name"/></td>
      <td><xsl:value-of select="category"/></td>
      <td class="price">$<xsl:value-of select="format-number(price, '0.00')"/></td>
      <td><xsl:value-of select="description"/></td>
      <td>
        <xsl:choose>
          <xsl:when test="inStock='true'">
            <span class="inStock">✓ In Stock</span>
          </xsl:when>
          <xsl:otherwise>
            <span class="outOfStock">✗ Out of Stock</span>
          </xsl:otherwise>
        </xsl:choose>
      </td>
      <td class="rating">
        <xsl:value-of select="format-number(rating, '0.0')"/>
        <xsl:text> / 5</xsl:text>
      </td>
    </tr>
  </xsl:template>

</xsl:stylesheet>
