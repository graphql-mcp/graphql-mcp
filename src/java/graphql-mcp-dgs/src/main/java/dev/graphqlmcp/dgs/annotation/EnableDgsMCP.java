package dev.graphqlmcp.dgs.annotation;

import dev.graphqlmcp.dgs.autoconfigure.DgsMCPAutoConfiguration;
import java.lang.annotation.Documented;
import java.lang.annotation.ElementType;
import java.lang.annotation.Retention;
import java.lang.annotation.RetentionPolicy;
import java.lang.annotation.Target;
import org.springframework.context.annotation.Import;

/** Enables graphql-mcp for a Netflix DGS application. */
@Target(ElementType.TYPE)
@Retention(RetentionPolicy.RUNTIME)
@Documented
@Import(DgsMCPAutoConfiguration.class)
public @interface EnableDgsMCP {}
